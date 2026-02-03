using System.Reflection;
using A2A;
using A2A.AspNetCore;
using A2AAgent.Agents;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection("AzureOpenAI"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Endpoint), "AzureOpenAI:Endpoint is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeploymentName), "AzureOpenAI:DeploymentName is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<TokenCredential>(_ => new AzureCliCredential());
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
    return new AzureOpenAIClient(new Uri(options.Endpoint), sp.GetRequiredService<TokenCredential>());
});

builder.Services.AddSingleton<A2AChatAgent>();

var agentId = builder.Configuration["Agent:OpenTelemetryAgentId"] ?? "a2a-chat-agent";
var telemetryBuilder = builder.Services.AddOpenTelemetry();
telemetryBuilder.ConfigureResource(resource =>
    resource
        .AddService(
            serviceName: "A2AAgent",
            serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString())
        .AddAttributes(new Dictionary<string, object>
        {
            ["service.instance.id"] = agentId,
            ["agent.id"] = agentId
        }));
telemetryBuilder.WithTracing(tracing =>
    tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());
telemetryBuilder.WithMetrics(metrics =>
    metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    telemetryBuilder.UseAzureMonitorExporter(options => options.ConnectionString = appInsightsConnectionString);
}

var app = builder.Build();

var basePath = builder.Configuration["Agent:BasePath"] ?? "/a2a";
if (!basePath.StartsWith('/'))
{
    basePath = $"/{basePath}";
}
var publicBaseUrl = builder.Configuration["Agent:PublicBaseUrl"] ?? "http://localhost:5230";
var agentUrl = $"{publicBaseUrl.TrimEnd('/')}{basePath}";

var chatAgent = app.Services.GetRequiredService<A2AChatAgent>();
var taskManager = new TaskManager();
chatAgent.Attach(taskManager, agentUrl);

app.MapA2A(taskManager, basePath);

app.MapGet("/.well-known/agent-card.json", () => Results.Json(chatAgent.GetAgentCard(agentUrl)));
app.MapGet($"{basePath}/.well-known/agent-card.json", () => Results.Json(chatAgent.GetAgentCard(agentUrl)));

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

internal sealed class OpenAIOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
}

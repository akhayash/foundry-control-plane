using System.Diagnostics;
using System.Reflection;
using A2A;
using A2A.AspNetCore;
using CustomAgent.Agents;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
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

// Agent Framework OpenTelemetry configuration
const string AgentSourceName = "CustomAgent.OpenAI";
var agentId = builder.Configuration["Agent:OpenTelemetryAgentId"] ?? "openai-compat-agent";

var telemetryBuilder = builder.Services.AddOpenTelemetry();
telemetryBuilder.ConfigureResource(resource =>
    resource
        .AddService(
            serviceName: "CustomAgent",
            serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString())
        .AddAttributes(new Dictionary<string, object>
        {
            ["service.instance.id"] = agentId,
            ["agent.id"] = agentId
        }));
telemetryBuilder.WithTracing(tracing =>
    tracing
        .AddSource(AgentSourceName)  // Custom agent source
        .AddSource("Microsoft.Agents.AI*")  // Agent Framework telemetry
        .AddSource("Microsoft.Extensions.AI*")  // Extensions AI telemetry
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());
telemetryBuilder.WithMetrics(metrics =>
    metrics
        .AddMeter(AgentSourceName)  // Custom agent metrics
        .AddMeter("Experimental.Microsoft.Extensions.AI")  // Extensions AI GenAI metrics (token usage, duration)
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

// Create ActivitySource for custom spans and Meter for custom metrics
var agentActivitySource = new ActivitySource(AgentSourceName);
var agentMeter = new System.Diagnostics.Metrics.Meter(AgentSourceName);
var inputTokensCounter = agentMeter.CreateCounter<long>("gen_ai.agent.input_tokens", "tokens", "Input tokens for GenAI operations");
var outputTokensCounter = agentMeter.CreateCounter<long>("gen_ai.agent.output_tokens", "tokens", "Output tokens for GenAI operations");
var totalTokensCounter = agentMeter.CreateCounter<long>("gen_ai.agent.total_tokens", "tokens", "Total tokens for GenAI operations");
var requestCounter = agentMeter.CreateCounter<long>("gen_ai.agent.request_count", "requests", "Request count for GenAI operations");

// OpenAI-compatible Chat Completions API - using Microsoft Agent Framework
app.MapPost("/v1/chat/completions", async (ChatCompletionRequest request, IServiceProvider sp) =>
{
    var openAIClient = sp.GetRequiredService<AzureOpenAIClient>();
    var options = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
    
    // Create AIAgent with OpenTelemetry instrumentation (Agent Framework pattern)
    // This automatically emits GenAI semantic conventions: invoke_agent, token usage, etc.
    // IMPORTANT: Set Id explicitly to match Foundry Portal's OpenTelemetry Agent ID
    const string chatAgentId = "openai-compat-agent";
    
    // Add BOTH singular and plural forms of agent ID attributes for Foundry compatibility
    // Foundry docs mention gen_ai.agents.id (plural) but OTel spec uses gen_ai.agent.id (singular)
    var currentActivity = Activity.Current;
    if (currentActivity != null)
    {
        currentActivity.SetTag("gen_ai.agents.id", chatAgentId);      // Plural form (Foundry docs)
        currentActivity.SetTag("gen_ai.agents.name", chatAgentId);    // Plural form (Foundry docs)
        currentActivity.SetTag("gen_ai.agent.id", chatAgentId);       // Singular form (OTel spec)
        currentActivity.SetTag("gen_ai.agent.name", chatAgentId);     // Singular form (OTel spec)
        currentActivity.SetTag("gen_ai.operation.name", "invoke_agent");
    }
    
    AIAgent agent = openAIClient
        .GetChatClient(options.DeploymentName)
        .AsIChatClient()  // Convert OpenAI ChatClient to Microsoft.Extensions.AI.IChatClient
        .AsAIAgent(new ChatClientAgentOptions
        {
            Id = chatAgentId,       // This sets gen_ai.agent.id - must match Foundry's OpenTelemetry Agent ID
            Name = chatAgentId,     // This sets gen_ai.agent.name
            Description = "OpenAI-compatible chat agent for Azure AI Foundry"
        })
        .AsBuilder()
        .UseOpenTelemetry(sourceName: AgentSourceName)
        .Build();
    
    // Convert request messages to Microsoft.Extensions.AI format
    var messages = request.Messages.Select<ChatMessageDto, Microsoft.Extensions.AI.ChatMessage>(m =>
        m.Role.ToLower() switch
        {
            "system" => new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, m.Content),
            "assistant" => new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, m.Content),
            _ => new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, m.Content)
        }).ToList();
    
    // Run agent - OpenTelemetry instrumentation is automatic via UseOpenTelemetry()
    var response = await agent.RunAsync(messages);
    
    // Extract token usage from response (if available)
    var inputTokens = response.Usage?.InputTokenCount ?? 0;
    var outputTokens = response.Usage?.OutputTokenCount ?? 0;
    var totalTokens = response.Usage?.TotalTokenCount ?? inputTokens + outputTokens;
    
    // Record custom metrics for Foundry Portal usage tracking
    var metricTags = new KeyValuePair<string, object?>[]
    {
        new("gen_ai.agents.id", chatAgentId),
        new("gen_ai.agents.name", chatAgentId),
        new("gen_ai.agent.id", chatAgentId),
        new("gen_ai.agent.name", chatAgentId),
        new("gen_ai.operation.name", "invoke_agent"),
        new("gen_ai.request.model", options.DeploymentName)
    };
    requestCounter.Add(1, metricTags);
    inputTokensCounter.Add(inputTokens, metricTags);
    outputTokensCounter.Add(outputTokens, metricTags);
    totalTokensCounter.Add(totalTokens, metricTags);
    
    return Results.Ok(new
    {
        id = $"chatcmpl-{response.ResponseId ?? Guid.NewGuid().ToString()}",
        @object = "chat.completion",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        model = options.DeploymentName,
        choices = new[]
        {
            new
            {
                index = 0,
                message = new
                {
                    role = "assistant",
                    content = response.Text
                },
                finish_reason = "stop"
            }
        },
        usage = new
        {
            prompt_tokens = inputTokens,
            completion_tokens = outputTokens,
            total_tokens = totalTokens
        }
    });
});

// Simple API endpoints for testing
app.MapGet("/api/weather/{city}", (string city) =>
{
    var weather = city.ToLower() switch
    {
        "tokyo" => new { city = "Tokyo", temperature = 15, condition = "Cloudy", humidity = 65 },
        "osaka" => new { city = "Osaka", temperature = 17, condition = "Sunny", humidity = 55 },
        "seattle" => new { city = "Seattle", temperature = 10, condition = "Rainy", humidity = 85 },
        _ => new { city, temperature = 20, condition = "Clear", humidity = 60 }
    };
    return Results.Ok(weather);
});

app.MapPost("/api/calculate", (CalculationRequest request) =>
{
    var result = request.Operation.ToLower() switch
    {
        "add" => request.A + request.B,
        "subtract" => request.A - request.B,
        "multiply" => request.A * request.B,
        "divide" => request.B != 0 ? request.A / request.B : double.NaN,
        _ => double.NaN
    };
    return Results.Ok(new { operation = request.Operation, a = request.A, b = request.B, result });
});

app.MapGet("/api/time", () =>
{
    var now = DateTimeOffset.UtcNow;
    return Results.Ok(new
    {
        utc = now.ToString("o"),
        unix = now.ToUnixTimeSeconds(),
        timezone = TimeZoneInfo.Local.DisplayName,
        localTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).ToString("o")
    });
});

app.MapGet("/api/quote", () =>
{
    var quotes = new[]
    {
        new { text = "The only way to do great work is to love what you do.", author = "Steve Jobs" },
        new { text = "Innovation distinguishes between a leader and a follower.", author = "Steve Jobs" },
        new { text = "Stay hungry, stay foolish.", author = "Steve Jobs" },
        new { text = "Code is like humor. When you have to explain it, it's bad.", author = "Cory House" },
        new { text = "First, solve the problem. Then, write the code.", author = "John Johnson" }
    };
    var random = new Random();
    return Results.Ok(quotes[random.Next(quotes.Length)]);
});

app.Run();

internal sealed class OpenAIOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
}

internal sealed record CalculationRequest(string Operation, double A, double B);

internal sealed record ChatCompletionRequest(
    List<ChatMessageDto> Messages,
    string? Model = null,
    double Temperature = 0.7,
    int MaxTokens = 800
);

internal sealed record ChatMessageDto(string Role, string Content);

// Hosted Agent - Azure AI Foundry Hosting Adapter
// =================================================
// このファイルは Adapter 部分のみを担当
// エージェントロジックは Agents/HostedDemoAgent.cs に分離
//
// Reference: https://github.com/microsoft-foundry/foundry-samples/tree/main/samples/csharp/hosted-agents

using Azure.AI.AgentServer.AgentFramework.Extensions;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using HostedAgent.Agents;

// ========================================
// Adapter 部分: 構成・認証・ホスティング
// ========================================

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Get configuration (Foundry上では AZURE_AI_PROJECT_ENDPOINT が自動注入)
var openAiEndpoint = GetConfigValue(configuration["AzureOpenAI:Endpoint"])
    ?? GetConfigValue(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"))
    ?? GetConfigValue(Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT"))
    ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured.");

var deploymentName = GetConfigValue(configuration["AzureOpenAI:DeploymentName"])
    ?? GetConfigValue(Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME"))
    ?? "gpt-4o-mini";

static string? GetConfigValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

Console.WriteLine($"Hosted Agent starting...");
Console.WriteLine($"OpenAI Endpoint: {openAiEndpoint}");
Console.WriteLine($"Deployment: {deploymentName}");

// Setup credential (ManagedIdentity for Foundry, AzureCli for local)
var credential = new ChainedTokenCredential(
    new ManagedIdentityCredential(),
    new AzureCliCredential()
);

// Create chat client for agent
var chatClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "Agents", configure: cfg => cfg.EnableSensitiveData = true)
    .Build();

// ========================================
// Agent 部分: エージェントロジック (分離済み)
// ========================================
// ここで別のエージェントに差し替え可能
// 例: new MyCustomAgent(chatClient).Build()
//     new MultiModelAgent(chatClient, otherClient).Build()
//     new OrchestratorAgent(subAgents).Build()

var agentBuilder = new HostedDemoAgent(chatClient);
var agent = agentBuilder.Build();

Console.WriteLine($"Agent: {agentBuilder.Name}");

// ========================================
// Hosting Adapter: Foundry AgentServer 連携
// ========================================
// ポート 8088 で Responses API プロトコルをホスト

Console.WriteLine("Hosted Agent ready on port 8088");
await agent.RunAIAgentAsync(telemetrySourceName: "Agents");

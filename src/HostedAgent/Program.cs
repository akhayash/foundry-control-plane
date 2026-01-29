// Hosted Agent - Azure AI Foundry Hosted Agent Demo
// =====================================================
// Microsoft Agent Framework + Azure AI AgentServer を使用して
// Foundry Agent Service 上でホスティングされるエージェント
//
// Reference: https://github.com/microsoft-foundry/foundry-samples/tree/main/samples/csharp/hosted-agents

using Azure.AI.AgentServer.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

// Build configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Get configuration from appsettings.json (with environment variable fallback)
// Foundry上では AZURE_AI_PROJECT_ENDPOINT が自動注入される
// Note: 空文字列もフォールバックするように string.IsNullOrEmpty を使用
var openAiEndpoint = GetConfigValue(configuration["AzureOpenAI:Endpoint"])
    ?? GetConfigValue(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"))
    ?? GetConfigValue(Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT"))
    ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured. Set AzureOpenAI:Endpoint in appsettings.json or AZURE_OPENAI_ENDPOINT environment variable.");

var deploymentName = GetConfigValue(configuration["AzureOpenAI:DeploymentName"])
    ?? GetConfigValue(Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME"))
    ?? "gpt-4o-mini";

// Helper function to treat empty strings as null
static string? GetConfigValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

Console.WriteLine($"Hosted Agent starting...");
Console.WriteLine($"OpenAI Endpoint: {openAiEndpoint}");
Console.WriteLine($"Deployment: {deploymentName}");

// Use ChainedTokenCredential for both Foundry (ManagedIdentity) and local (AzureCli) environments
var credential = new ChainedTokenCredential(
    new ManagedIdentityCredential(),  // Foundry上で優先
    new AzureCliCredential()          // ローカル開発用フォールバック
);

// Create chat client
var chatClient = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "Agents", configure: (cfg) => cfg.EnableSensitiveData = true)
    .Build();

// Create agent with instructions
var agent = new ChatClientAgent(chatClient,
    name: "HostedDemoAgent",
    instructions: @"あなたは Azure AI Foundry 上で動作する Hosted Agent です。
ユーザーの質問に親切に回答してください。

このエージェントの特徴:
- Azure AI Foundry Agent Service でコンテナとしてホスティング
- Microsoft Agent Framework を使用
- Azure OpenAI モデルを利用

日本語で回答してください。")
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "Agents", configure: (cfg) => cfg.EnableSensitiveData = true)
    .Build();

// Run agent with Azure AI AgentServer hosting adapter
Console.WriteLine("Hosted Agent ready on port 8088");
await agent.RunAIAgentAsync(telemetrySourceName: "Agents");

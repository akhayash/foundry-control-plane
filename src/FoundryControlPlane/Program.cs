// Azure AI Foundry Control Plane Demo
// =====================================
// Microsoft Agent Framework 1.0.0 GA + .NET 10.0 LTS
//
// Usage:
//   dotnet run                          # インタラクティブモード
//   dotnet run -- --auto --type prompt  # 自動モード（Prompt Agent）
//   dotnet run -- --auto --type workflow # 自動モード（Workflow Agent）
//   dotnet run -- --auto --type workflow --no-cleanup # 削除せず残す

using Azure.Identity;
using FoundryControlPlane.Agents;
using FoundryControlPlane.Configuration;
using FoundryControlPlane.Runners;
using FoundryControlPlane.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// ===== バナー表示 =====
var rule = new Rule("[bold cyan]Azure AI Foundry Control Plane Demo[/]");
rule.Style = Style.Parse("cyan");
AnsiConsole.Write(rule);
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]Microsoft Agent Framework 1.0.0 GA[/]");
AnsiConsole.MarkupLine("[dim].NET 10.0 LTS[/]");
AnsiConsole.WriteLine();

// ===== 構成読み込み =====
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// ===== DI コンテナ設定 =====
var services = new ServiceCollection();

// Logging
services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));
    builder.AddConsole();
});

// Configuration
services.AddSingleton<IConfiguration>(configuration);
services.Configure<AzureAISettings>(configuration.GetSection("AzureAI"));

// Azure Credentials
services.AddSingleton<AzureCliCredential>();

// Telemetry
services.AddSingleton<TelemetryService>();

// Agents
services.AddSingleton<AgentServiceStrategy>();
services.AddSingleton<WorkflowAgentStrategy>();

// Runners
services.AddTransient<AgentServiceRunner>();
services.AddTransient<WorkflowRunner>();

await using var serviceProvider = services.BuildServiceProvider();

// ===== テレメトリ初期化 =====
var telemetry = serviceProvider.GetRequiredService<TelemetryService>();
await telemetry.InitializeAsync();

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Azure AI Foundry Control Plane Demo を開始します");

// ===== コマンドライン引数解析 =====
bool autoMode = args.Contains("--auto");
bool noCleanup = args.Contains("--no-cleanup");
string? typeArg = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--type")
    {
        typeArg = args[i + 1].ToLower();
        break;
    }
}

if (autoMode)
{
    AnsiConsole.MarkupLine("[dim]自動モードで実行中...[/]");
}

// ===== メニュー選択 =====
string agentType;
if (autoMode && typeArg != null)
{
    agentType = typeArg switch
    {
        "prompt" => "1. Prompt Agent (単一エージェント)",
        "workflow" => "2. Workflow Agent (マルチステップ)",
        _ => "3. 終了"
    };
    AnsiConsole.MarkupLine($"[dim]選択: {agentType}[/]");
}
else
{
    agentType = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[bold]エージェントタイプを選択してください:[/]")
            .PageSize(5)
            .AddChoices(new[]
            {
                "1. Prompt Agent (単一エージェント)",
                "2. Workflow Agent (マルチステップ)",
                "3. 終了"
            }));
}

// ===== メイン実行 =====
try
{
    switch (agentType)
    {
        case "1. Prompt Agent (単一エージェント)":
            var promptRunner = serviceProvider.GetRequiredService<AgentServiceRunner>();
            await promptRunner.RunAsync();
            break;

        case "2. Workflow Agent (マルチステップ)":
            var workflowRunner = serviceProvider.GetRequiredService<WorkflowRunner>();
            await workflowRunner.RunAsync(autoMode, !noCleanup);
            break;

        case "3. 終了":
            AnsiConsole.MarkupLine("[dim]終了します[/]");
            break;
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "致命的なエラーが発生しました");
    AnsiConsole.WriteException(ex);
    return 1;
}

return 0;

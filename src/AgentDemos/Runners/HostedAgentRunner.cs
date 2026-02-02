// Hosted Agent Runner
// Hosted Agent のデプロイと呼び出しを行うランナー
// azd / SDK によるデプロイ、テスト実行をサポート

using Azure.Identity;
using AgentDemos.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Diagnostics;

namespace AgentDemos.Runners;

/// <summary>
/// Hosted Agent の動作確認ランナー
/// コンテナ化されたエージェントを Foundry にデプロイして呼び出す
/// </summary>
public class HostedAgentRunner
{
    private readonly ILogger<HostedAgentRunner> _logger;
    private readonly AzureAISettings _settings;

    public HostedAgentRunner(
        ILogger<HostedAgentRunner> logger,
        IOptions<AzureAISettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Hosted Agent デモを実行
    /// </summary>
    public async Task RunAsync(bool autoMode = false)
    {
        AnsiConsole.MarkupLine("[bold blue]Hosted Agent 動作確認[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Hosted Agent は以下のコンポーネントで構成されています:[/]");
        AnsiConsole.MarkupLine("  • [cyan]Microsoft Agent Framework[/] - エージェントロジック");
        AnsiConsole.MarkupLine("  • [cyan]Hosting Adapter[/] - HTTP サービス化");
        AnsiConsole.MarkupLine("  • [cyan]Docker コンテナ[/] - パッケージング");
        AnsiConsole.MarkupLine("  • [cyan]Azure Container Registry[/] - イメージ格納");
        AnsiConsole.MarkupLine("  • [cyan]Foundry Agent Service[/] - マネージド実行");
        AnsiConsole.WriteLine();

        // メニュー選択
        string action;
        if (autoMode)
        {
            action = "1. ローカルテスト (Docker 不要)";
            AnsiConsole.MarkupLine($"[dim]選択: {action}[/]");
        }
        else
        {
            action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]実行するアクションを選択してください:[/]")
                    .PageSize(6)
                    .AddChoices(new[]
                    {
                        "1. ローカルテスト (Docker 不要)",
                        "2. Docker ビルド & ローカル実行",
                        "3. ACR にプッシュ",
                        "4. Foundry にデプロイ",
                        "5. デプロイ済み Hosted Agent を呼び出し",
                        "6. 戻る"
                    }));
        }

        try
        {
            switch (action)
            {
                case "1. ローカルテスト (Docker 不要)":
                    await RunLocalTestAsync();
                    break;

                case "2. Docker ビルド & ローカル実行":
                    await RunDockerLocalAsync();
                    break;

                case "3. ACR にプッシュ":
                    await PushToAcrAsync();
                    break;

                case "4. Foundry にデプロイ":
                    await DeployToFoundryAsync();
                    break;

                case "5. デプロイ済み Hosted Agent を呼び出し":
                    await InvokeHostedAgentAsync();
                    break;

                case "6. 戻る":
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hosted Agent 操作中にエラー");
            AnsiConsole.MarkupLine($"[red]エラー: {ex.Message}[/]");
            AnsiConsole.WriteException(ex);
        }
    }

    /// <summary>
    /// ローカルで dotnet run によるテスト
    /// </summary>
    private async Task RunLocalTestAsync()
    {
        AnsiConsole.MarkupLine("[yellow]ローカルテストを開始...[/]");
        AnsiConsole.MarkupLine("[dim]Ctrl+C で停止[/]");
        AnsiConsole.WriteLine();

        var projectPath = Path.Combine(GetRepoRoot(), "src", "HostedAgent");
        if (!Directory.Exists(projectPath))
        {
            AnsiConsole.MarkupLine("[red]HostedAgent プロジェクトが見つかりません[/]");
            AnsiConsole.MarkupLine($"[dim]期待されるパス: {projectPath}[/]");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run",
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["AZURE_AI_PROJECT_ENDPOINT"] = _settings.FoundryEndpoint,
                ["MODEL_NAME"] = "gpt-4o"
            }
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                AnsiConsole.MarkupLine($"[dim]{e.Data}[/]");
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                AnsiConsole.MarkupLine($"[red]{e.Data}[/]");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        AnsiConsole.MarkupLine("[green]エージェント起動中...[/]");
        AnsiConsole.MarkupLine("[dim]http://localhost:8088/health で確認可能[/]");
        AnsiConsole.MarkupLine("[dim]http://localhost:8088/responses で呼び出し可能[/]");

        await process.WaitForExitAsync();
    }

    /// <summary>
    /// Docker でローカル実行
    /// </summary>
    private async Task RunDockerLocalAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Docker ビルド & 実行...[/]");

        var projectPath = Path.Combine(GetRepoRoot(), "src", "HostedAgent");

        // Build
        await RunCommandAsync("docker", $"build -t hosted-agent:local {projectPath}");

        AnsiConsole.MarkupLine("[green]Docker イメージをビルドしました[/]");
        AnsiConsole.WriteLine();

        // Run
        AnsiConsole.MarkupLine("[yellow]コンテナを起動中...[/]");
        AnsiConsole.MarkupLine("[dim]Ctrl+C で停止[/]");

        var envArgs = $"-e AZURE_AI_PROJECT_ENDPOINT={_settings.FoundryEndpoint} -e MODEL_NAME=gpt-4o";
        await RunCommandAsync("docker", $"run -it --rm -p 8088:8088 {envArgs} hosted-agent:local");
    }

    /// <summary>
    /// ACR にプッシュ
    /// </summary>
    private async Task PushToAcrAsync()
    {
        AnsiConsole.MarkupLine("[yellow]ACR にプッシュ...[/]");
        AnsiConsole.MarkupLine("[dim]deploy-hosted-agent.ps1 を使用してください[/]");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Foundry にデプロイ
    /// </summary>
    private async Task DeployToFoundryAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Foundry にデプロイ...[/]");
        AnsiConsole.MarkupLine("[dim]deploy-hosted-agent.ps1 を使用してください[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("実行コマンド:");
        AnsiConsole.MarkupLine("[cyan]  ./scripts/deploy-hosted-agent.ps1 -ResourceGroup <rg-name>[/]");
        await Task.CompletedTask;
    }

    /// <summary>
    /// デプロイ済み Hosted Agent を呼び出し
    /// </summary>
    private async Task InvokeHostedAgentAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Hosted Agent を呼び出し中...[/]");

        var agentName = AnsiConsole.Ask<string>("Agent 名を入力:", "demo-hosted-agent");
        var message = AnsiConsole.Ask<string>("メッセージを入力:", "こんにちは！");

        AnsiConsole.MarkupLine("[dim]Azure AI Projects SDK を使用して呼び出し中...[/]");

        // Python SDK を使用（C# SDK は Hosted Agent invocation 未サポートの可能性）
        var pythonScript = $@"
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import AgentReference
from azure.identity import DefaultAzureCredential

client = AIProjectClient(endpoint=""{_settings.FoundryEndpoint}"", credential=DefaultAzureCredential())
agent = client.agents.retrieve(agent_name=""{agentName}"")

openai_client = client.get_openai_client()
response = openai_client.responses.create(
    input=[{{""role"": ""user"", ""content"": ""{message}""}}],
    extra_body={{""agent"": AgentReference(name=agent.name).as_dict()}}
)
print(response.output_text)
";

        var tempFile = Path.GetTempFileName() + ".py";
        await File.WriteAllTextAsync(tempFile, pythonScript);

        try
        {
            var result = await RunCommandAsync("python", tempFile, captureOutput: true);

            var panel = new Panel(result)
            {
                Header = new PanelHeader("[bold]Hosted Agent の応答[/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]呼び出し失敗: {ex.Message}[/]");
            AnsiConsole.MarkupLine("[dim]azure-ai-projects パッケージがインストールされているか確認してください[/]");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static string GetRepoRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (current != null && !Directory.Exists(Path.Combine(current, ".git")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current ?? Directory.GetCurrentDirectory();
    }

    private static async Task<string> RunCommandAsync(string command, string arguments, bool captureOutput = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = captureOutput
        };

        using var process = Process.Start(psi)!;

        string output = "";
        if (captureOutput)
        {
            output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            if (!string.IsNullOrEmpty(error))
            {
                throw new Exception(error);
            }
        }

        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !captureOutput)
        {
            throw new Exception($"Command failed with exit code {process.ExitCode}");
        }

        return output;
    }
}

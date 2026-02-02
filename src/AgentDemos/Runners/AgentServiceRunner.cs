// Agent Service 動作確認
// 手動実行用のデモランナー
// 新しい API (Azure.AI.Agents 2.0) を使用

using AgentDemos.Agents;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AgentDemos.Runners;

/// <summary>
/// Agent Service の動作確認ランナー
/// </summary>
public class AgentServiceRunner
{
    private readonly ILogger<AgentServiceRunner> _logger;
    private readonly AgentServiceStrategy _strategy;

    public AgentServiceRunner(
        ILogger<AgentServiceRunner> logger,
        AgentServiceStrategy strategy)
    {
        _logger = logger;
        _strategy = strategy;
    }

    /// <summary>
    /// Agent Service を1つ登録して動作確認
    /// </summary>
    /// <param name="autoMode">自動モード（確認プロンプトをスキップ）</param>
    /// <param name="cleanup">終了時にエージェントを削除するか</param>
    public async Task RunAsync(bool autoMode = false, bool cleanup = true)
    {
        AnsiConsole.MarkupLine("[bold cyan]Agent Service 動作確認 (新API)[/]");
        AnsiConsole.WriteLine();

        // 一意なエージェント名（autoモード時はタイムスタンプ追加）
        string suffix = autoMode ? $"-{DateTime.Now:HHmmss}" : "";
        string agentName = $"demo-prompt-agent{suffix}";

        try
        {
            // 1. 作成
            AnsiConsole.MarkupLine("[yellow]1. Agent Service を作成...[/]");

            var agentVersion = await _strategy.CreateAgentAsync(
                agentName,
                "あなたは親切なアシスタントです。ユーザーの質問に丁寧に答えてください。");

            AnsiConsole.MarkupLine($"[green]✓ 作成成功[/]");
            AnsiConsole.MarkupLine($"  Name: [cyan]{agentVersion.Name}[/]");
            AnsiConsole.MarkupLine($"  Version: {agentVersion.Version}");
            AnsiConsole.MarkupLine($"  ID: {agentVersion.Id}");
            AnsiConsole.WriteLine();

            // 2. バージョン一覧取得
            AnsiConsole.MarkupLine("[yellow]2. エージェントバージョン一覧を取得...[/]");

            var versions = await _strategy.ListAgentVersionsAsync(agentName);
            AnsiConsole.MarkupLine($"[green]✓ {versions.Count} 個のバージョンを取得[/]");
            foreach (var v in versions)
            {
                AnsiConsole.MarkupLine($"  - Version: {v.Version}, ID: {v.Id}");
            }
            AnsiConsole.WriteLine();

            // 3. 実行
            bool runAgent = autoMode || AnsiConsole.Confirm("エージェントを実行しますか?", true);
            if (runAgent)
            {
                AnsiConsole.MarkupLine("[yellow]3. エージェントを実行...[/]");
                var response = await _strategy.TestAgentAsync(agentName, "こんにちは！自己紹介をしてください。");

                var panel = new Panel(response)
                {
                    Header = new PanelHeader("[bold]エージェントの応答[/]"),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }

            // 4. 削除
            bool doCleanup = autoMode ? cleanup : AnsiConsole.Confirm("作成したエージェントを削除しますか?", true);
            if (doCleanup)
            {
                AnsiConsole.MarkupLine("[yellow]4. エージェントを削除...[/]");
                await _strategy.DeleteAgentAsync(agentName);
                AnsiConsole.MarkupLine($"[green]✓ 削除成功[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold green]完了[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "実行中にエラーが発生");
            AnsiConsole.MarkupLine($"[red]エラー: {ex.Message}[/]");
            AnsiConsole.WriteException(ex);

            // クリーンアップ
            bool doErrorCleanup = autoMode ? cleanup : AnsiConsole.Confirm("作成されたエージェントを削除しますか?", false);
            if (doErrorCleanup)
            {
                try
                {
                    await _strategy.DeleteAgentAsync(agentName);
                    AnsiConsole.MarkupLine("[green]✓ クリーンアップ完了[/]");
                }
                catch
                {
                    // 削除失敗は無視
                }
            }
        }
    }
}

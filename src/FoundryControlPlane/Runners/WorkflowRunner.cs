// Workflow Agent 動作確認
// 手動実行用のデモランナー
// 新しい API (Azure.AI.Agents 2.0) を使用

using FoundryControlPlane.Agents;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace FoundryControlPlane.Runners;

/// <summary>
/// Workflow Agent の動作確認ランナー
/// </summary>
public class WorkflowRunner
{
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly WorkflowAgentStrategy _workflowStrategy;
    private readonly AgentServiceStrategy _promptStrategy;

    public WorkflowRunner(
        ILogger<WorkflowRunner> logger,
        WorkflowAgentStrategy workflowStrategy,
        AgentServiceStrategy promptStrategy)
    {
        _logger = logger;
        _workflowStrategy = workflowStrategy;
        _promptStrategy = promptStrategy;
    }

    /// <summary>
    /// Workflow Agent を登録して動作確認
    /// </summary>
    /// <param name="autoMode">自動モード（確認なしで実行）</param>
    /// <param name="cleanup">終了時にエージェントを削除するか</param>
    public async Task RunAsync(bool autoMode = false, bool cleanup = true)
    {
        AnsiConsole.MarkupLine("[bold magenta]Workflow Agent 動作確認 (新API)[/]");
        if (autoMode)
        {
            AnsiConsole.MarkupLine("[dim]自動モード: 確認なしで実行します[/]");
        }
        AnsiConsole.WriteLine();

        // 一意なエージェント名（テスト用にタイムスタンプ追加）
        string suffix = autoMode ? $"-{DateTime.Now:HHmmss}" : "";
        string subAgentName = $"demo-workflow-sub-agent{suffix}";
        string workflowAgentName = $"demo-workflow-agent{suffix}";

        try
        {
            // 1. まずSub Agent を作成（Workflowから参照される）
            AnsiConsole.MarkupLine("[yellow]1. Sub Agent を作成 (Workflowから参照用)...[/]");

            var subAgent = await _promptStrategy.CreateAgentAsync(
                subAgentName,
                "あなたは親切なアシスタントです。ユーザーの質問に丁寧に日本語で答えてください。");

            AnsiConsole.MarkupLine($"[green]✓ Sub Agent 作成成功[/]");
            AnsiConsole.MarkupLine($"  Name: [cyan]{subAgent.Name}[/]");
            AnsiConsole.MarkupLine($"  Version: {subAgent.Version}");
            AnsiConsole.WriteLine();

            // 2. Workflow Agent を作成
            AnsiConsole.MarkupLine("[yellow]2. Workflow Agent を作成...[/]");

            // サンプルYAMLを表示
            var workflowYaml = WorkflowAgentStrategy.GetSampleWorkflowYaml(subAgentName);
            var yamlPanel = new Panel(workflowYaml)
            {
                Header = new PanelHeader("[bold]Workflow YAML[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("dim")
            };
            AnsiConsole.Write(yamlPanel);
            AnsiConsole.WriteLine();

            var workflowAgent = await _workflowStrategy.CreateAgentAsync(
                workflowAgentName,
                workflowYaml);

            AnsiConsole.MarkupLine($"[green]✓ Workflow Agent 作成成功[/]");
            AnsiConsole.MarkupLine($"  Name: [cyan]{workflowAgent.Name}[/]");
            AnsiConsole.MarkupLine($"  Version: {workflowAgent.Version}");
            AnsiConsole.MarkupLine($"  ID: {workflowAgent.Id}");
            AnsiConsole.MarkupLine($"  DefinitionType: [magenta]{workflowAgent.Definition?.GetType().Name ?? "null"}[/]");
            AnsiConsole.WriteLine();

            // 3. バージョン一覧取得
            AnsiConsole.MarkupLine("[yellow]3. エージェントバージョン一覧を取得...[/]");

            var versions = await _workflowStrategy.ListAgentVersionsAsync(workflowAgentName);
            AnsiConsole.MarkupLine($"[green]✓ {versions.Count} 個のバージョンを取得[/]");
            foreach (var v in versions)
            {
                AnsiConsole.MarkupLine($"  - Version: {v.Version}, ID: {v.Id}");
            }
            AnsiConsole.WriteLine();

            // 4. 実行
            bool shouldExecute = autoMode || AnsiConsole.Confirm("Workflow Agent を実行しますか?", true);
            if (shouldExecute)
            {
                AnsiConsole.MarkupLine("[yellow]4. Workflow Agent を実行...[/]");
                var response = await _workflowStrategy.TestAgentAsync(
                    workflowAgentName,
                    "今日の天気について教えてください。");

                var responseText = response ?? "(応答なし)";
                var panel = new Panel(responseText)
                {
                    Header = new PanelHeader("[bold]Workflow Agent の応答[/]"),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }

            // 5. 削除
            bool shouldDelete = cleanup && (autoMode || AnsiConsole.Confirm("作成したエージェントを削除しますか?", true));
            if (shouldDelete)
            {
                AnsiConsole.MarkupLine("[yellow]5. エージェントを削除...[/]");

                await _workflowStrategy.DeleteAgentAsync(workflowAgentName);
                AnsiConsole.MarkupLine($"[green]✓ Workflow Agent 削除成功[/]");

                await _promptStrategy.DeleteAgentAsync(subAgentName);
                AnsiConsole.MarkupLine($"[green]✓ Sub Agent 削除成功[/]");
            }
            else if (!cleanup)
            {
                AnsiConsole.MarkupLine("[dim]--no-cleanup: エージェントを残します（Portalで確認可能）[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold green]完了[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "実行中にエラーが発生");
            AnsiConsole.MarkupLine($"[red]エラー: {ex.Message}[/]");
            AnsiConsole.WriteException(ex);

            // クリーンアップ（自動モードではエラー時も削除試行）
            bool shouldCleanup = autoMode || AnsiConsole.Confirm("作成されたエージェントを削除しますか?", false);
            if (shouldCleanup)
            {
                try
                {
                    await _workflowStrategy.DeleteAgentAsync(workflowAgentName);
                    await _promptStrategy.DeleteAgentAsync(subAgentName);
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

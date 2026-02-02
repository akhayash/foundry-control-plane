// Agent Strategy インターフェース
// 各エージェントタイプごとに異なる登録・実行ロジックを実装
// 新しい API (Azure.AI.Agents 2.0) を使用

using Azure.AI.Agents;

namespace AgentDemos.Agents;

/// <summary>
/// エージェントの種類
/// </summary>
public enum AgentType
{
    /// <summary>Agent Service (Foundry 標準)</summary>
    AgentService,

    /// <summary>Foundry Hosted Agent (フルマネージド)</summary>
    FoundryHosted,

    /// <summary>Custom Agent (カスタムツール付き)</summary>
    Custom,

    /// <summary>Workflow Agent (GroupChat 用)</summary>
    Workflow
}

/// <summary>
/// エージェント登録のStrategyインターフェース
/// </summary>
public interface IAgentStrategy
{
    /// <summary>
    /// エージェントタイプ
    /// </summary>
    AgentType Type { get; }

    /// <summary>
    /// エージェントを作成
    /// </summary>
    Task<AgentVersion> CreateAgentAsync(
        string name,
        string instructions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// エージェントバージョン一覧を取得
    /// </summary>
    Task<IReadOnlyList<AgentVersion>> ListAgentVersionsAsync(
        string agentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// エージェントを削除
    /// </summary>
    Task<bool> DeleteAgentAsync(
        string agentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// エージェントをテスト実行
    /// </summary>
    Task<string> TestAgentAsync(
        string agentName,
        string testMessage,
        CancellationToken cancellationToken = default);
}

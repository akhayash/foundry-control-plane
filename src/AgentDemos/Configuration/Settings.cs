// Configuration Settings
// =======================

namespace AgentDemos.Configuration;

/// <summary>
/// Azure AI 関連の設定
/// </summary>
public class AzureAISettings
{
    /// <summary>
    /// Azure AI Foundry エンドポイント
    /// </summary>
    public string FoundryEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// プロジェクト名
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// エージェント設定
    /// </summary>
    public AgentSettings Agents { get; set; } = new();
}

/// <summary>
/// エージェントタイプごとの設定
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// Agent Service 設定
    /// </summary>
    public AgentTypeSettings AgentService { get; set; } = new() { Model = "gpt-4o" };

    /// <summary>
    /// Foundry Hosted Agent 設定
    /// </summary>
    public AgentTypeSettings FoundryHosted { get; set; } = new() { Model = "gpt-4o-mini" };

    /// <summary>
    /// Custom Agent 設定
    /// </summary>
    public AgentTypeSettings Custom { get; set; } = new() { Model = "gpt-4o" };

    /// <summary>
    /// Workflow Agent 設定
    /// </summary>
    public AgentTypeSettings Workflow { get; set; } = new() { Model = "gpt-4o" };
}

/// <summary>
/// エージェントタイプの設定
/// </summary>
public class AgentTypeSettings
{
    /// <summary>
    /// 使用するモデル名
    /// </summary>
    public string Model { get; set; } = string.Empty;
}

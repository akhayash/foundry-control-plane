// HostedDemoAgent - エージェントロジック
// ===========================================
// Adapter部分から独立したエージェント実装
// 複雑なビジネスロジック、マルチモデル、外部API連携などを実装可能

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedAgent.Agents;

/// <summary>
/// Hosted Agent のコアロジック
/// Agent Service では実現できない複雑な処理を実装
/// </summary>
public class HostedDemoAgent
{
    private readonly IChatClient _chatClient;
    private readonly string _name;
    private readonly string _instructions;

    public HostedDemoAgent(IChatClient chatClient, string? name = null, string? instructions = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _name = name ?? "HostedDemoAgent";
        _instructions = instructions ?? DefaultInstructions;
    }

    private const string DefaultInstructions = @"あなたは Azure AI Foundry 上で動作する Hosted Agent です。
ユーザーの質問に親切に回答してください。

このエージェントの特徴:
- Azure AI Foundry Agent Service でコンテナとしてホスティング
- Microsoft Agent Framework を使用
- Azure OpenAI モデルを利用

日本語で回答してください。";

    /// <summary>
    /// ChatClientAgent を構築
    /// Adapter側でOpenTelemetryを適用してRunAIAgentAsync()を呼ぶ
    /// </summary>
    public ChatClientAgent Build()
    {
        return new ChatClientAgent(_chatClient, name: _name, instructions: _instructions);
    }

    /// <summary>
    /// エージェント名
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// エージェントの指示
    /// </summary>
    public string Instructions => _instructions;
}

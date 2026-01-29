// Agent Service Strategy
// Azure AI Foundry の Agent Service を使用したエージェント登録
// 新しい API (Azure.AI.Agents 2.0) を使用

using Azure.AI.Agents;
using Azure.Identity;
using FoundryControlPlane.Configuration;
using FoundryControlPlane.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace FoundryControlPlane.Agents;

/// <summary>
/// Agent Service の登録・実行ロジック（新API）
/// </summary>
public class AgentServiceStrategy : IAgentStrategy
{
    private readonly ILogger<AgentServiceStrategy> _logger;
    private readonly AzureAISettings _settings;
    private readonly TelemetryService _telemetry;
    private readonly AgentClient _agentClient;
    private readonly string _model;

    public AgentType Type => AgentType.AgentService;

    public AgentServiceStrategy(
        ILogger<AgentServiceStrategy> logger,
        IOptions<AzureAISettings> settings,
        TelemetryService telemetry,
        AzureCliCredential credential)
    {
        _logger = logger;
        _settings = settings.Value;
        _telemetry = telemetry;
        _model = _settings.Agents.AgentService.Model;

        _agentClient = new AgentClient(
            new Uri(_settings.FoundryEndpoint),
            credential);
    }

    /// <inheritdoc/>
    public async Task<AgentVersion> CreateAgentAsync(
        string name,
        string instructions,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetry.TraceAgentOperation("Create", name);

        try
        {
            _logger.LogInformation("Agent Service '{Name}' を作成中... (Model: {Model})", name, _model);

            var agentDefinition = new PromptAgentDefinition(_model)
            {
                Instructions = instructions
            };

            var agentVersionResult = await _agentClient.CreateAgentVersionAsync(
                name,
                options: new AgentVersionCreationOptions(agentDefinition),
                cancellationToken: cancellationToken);
            var agentVersion = agentVersionResult.Value;

            _logger.LogInformation("Agent Service '{Name}' (Version: {Version}) を作成しました",
                agentVersion.Name, agentVersion.Version);

            activity?.SetTag("agent.name", agentVersion.Name);
            activity?.SetTag("agent.version", agentVersion.Version);
            return agentVersion;
        }
        catch (Exception ex)
        {
            _telemetry.RecordError(activity, ex);
            _logger.LogError(ex, "Agent Service '{Name}' の作成に失敗", name);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AgentVersion>> ListAgentVersionsAsync(
        string agentName,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetry.TraceAgentOperation("List", agentName);

        try
        {
            var versions = new List<AgentVersion>();
            await foreach (var v in _agentClient.GetAgentVersionsAsync(agentName, cancellationToken: cancellationToken))
            {
                versions.Add(v);
            }

            _logger.LogInformation("{Count} 個のバージョンを取得 (Agent: {Name})", versions.Count, agentName);
            activity?.SetTag("agent.version.count", versions.Count);

            return versions;
        }
        catch (Exception ex)
        {
            _telemetry.RecordError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAgentAsync(
        string agentName,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetry.TraceAgentOperation("Delete", agentName);

        try
        {
            _logger.LogInformation("Agent '{Name}' を削除中...", agentName);

            await _agentClient.DeleteAgentAsync(agentName, cancellationToken: cancellationToken);

            _logger.LogInformation("Agent '{Name}' を削除しました", agentName);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Agent '{Name}' は既に存在しません", agentName);
            return false;
        }
        catch (Exception ex)
        {
            _telemetry.RecordError(activity, ex);
            _logger.LogError(ex, "Agent '{Name}' の削除に失敗", agentName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string> TestAgentAsync(
        string agentName,
        string testMessage,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetry.TraceAgentOperation("Test", agentName);

        try
        {
            _logger.LogInformation("Agent '{Name}' をテスト実行中...", agentName);

            // OpenAI Client を取得
            OpenAIClient openAIClient = _agentClient.GetOpenAIClient();
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_model);

            // Conversation を作成
            var conversationResult = await _agentClient.GetConversationClient().CreateConversationAsync(
                new AgentConversationCreationOptions(),
                cancellationToken);
            var conversation = conversationResult.Value;

            // ResponseCreationOptions にエージェント参照を設定
            var responseOptions = new ResponseCreationOptions();
            responseOptions.SetAgentReference(agentName);
            responseOptions.SetConversationReference(conversation.Id);

            // エージェントとチャット
            var items = new List<ResponseItem> { ResponseItem.CreateUserMessageItem(testMessage) };
            OpenAIResponse response = await responseClient.CreateResponseAsync(items, responseOptions, cancellationToken);

            string outputText = response.GetOutputText();
            _logger.LogInformation("Agent '{Name}' から応答を受信", agentName);

            return outputText;
        }
        catch (Exception ex)
        {
            _telemetry.RecordError(activity, ex);
            _logger.LogError(ex, "Agent '{Name}' のテスト実行に失敗", agentName);
            throw;
        }
    }
}

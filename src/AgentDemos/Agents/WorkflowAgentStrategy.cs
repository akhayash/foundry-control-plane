// Workflow Agent Strategy
// Azure AI Foundry の Workflow Agent を使用したマルチステップエージェント
// 新しい API (Azure.AI.Agents 2.0) を使用

using Azure.AI.Agents;
using Azure.Identity;
using AgentDemos.Configuration;
using AgentDemos.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace AgentDemos.Agents;

/// <summary>
/// Workflow Agent の登録・実行ロジック（新API）
/// YAMLベースのワークフロー定義を使用
/// </summary>
public class WorkflowAgentStrategy : IAgentStrategy
{
    private readonly ILogger<WorkflowAgentStrategy> _logger;
    private readonly AzureAISettings _settings;
    private readonly TelemetryService _telemetry;
    private readonly AgentClient _agentClient;
    private readonly string _model;

    public AgentType Type => AgentType.Workflow;

    public WorkflowAgentStrategy(
        ILogger<WorkflowAgentStrategy> logger,
        IOptions<AzureAISettings> settings,
        TelemetryService telemetry,
        AzureCliCredential credential)
    {
        _logger = logger;
        _settings = settings.Value;
        _telemetry = telemetry;
        _model = _settings.Agents.Workflow.Model;

        _agentClient = new AgentClient(
            new Uri(_settings.FoundryEndpoint),
            credential);
    }

    /// <summary>
    /// ワークフローYAMLからエージェントを作成
    /// </summary>
    public async Task<AgentVersion> CreateAgentAsync(
        string name,
        string workflowYaml,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetry.TraceAgentOperation("CreateWorkflow", name);

        try
        {
            _logger.LogInformation("Workflow Agent '{Name}' を作成中...", name);

            var workflowDefinition = WorkflowAgentDefinition.FromYaml(workflowYaml);

            var agentVersionResult = await _agentClient.CreateAgentVersionAsync(
                name,
                options: new AgentVersionCreationOptions(workflowDefinition)
                {
                    Description = "Workflow Agent created by Control Plane Demo"
                },
                cancellationToken: cancellationToken);
            var agentVersion = agentVersionResult.Value;

            _logger.LogInformation("Workflow Agent '{Name}' (Version: {Version}) を作成しました",
                agentVersion.Name, agentVersion.Version);

            activity?.SetTag("agent.name", agentVersion.Name);
            activity?.SetTag("agent.version", agentVersion.Version);
            activity?.SetTag("agent.type", "workflow");
            return agentVersion;
        }
        catch (Exception ex)
        {
            _telemetry.RecordError(activity, ex);
            _logger.LogError(ex, "Workflow Agent '{Name}' の作成に失敗", name);
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
        using var activity = _telemetry.TraceAgentOperation("TestWorkflow", agentName);

        try
        {
            _logger.LogInformation("Workflow Agent '{Name}' をテスト実行中...", agentName);

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
            _logger.LogInformation("Workflow Agent '{Name}' から応答を受信", agentName);

            return outputText;
        }
        catch (Exception ex)
        {
            _telemetry.RecordError(activity, ex);
            _logger.LogError(ex, "Workflow Agent '{Name}' のテスト実行に失敗", agentName);
            throw;
        }
    }

    /// <summary>
    /// サンプルのワークフローYAMLを取得（デモ用の豊富なワークフロー）
    /// </summary>
    public static string GetSampleWorkflowYaml(string promptAgentName)
    {
        // 式（expression）は引用符なしで記述する必要がある
        // InvokeAzureAgentの応答はLocal.AgentResponseに格納される
        return $@"kind: workflow
trigger:
  kind: OnConversationStart
  id: demo_workflow
  actions:
    - kind: SendActivity
      id: welcome_message
      activity: 🚀 ワークフローエージェントへようこそ！
    - kind: SetVariable
      id: init_message
      variable: Local.UserInput
      value: =UserMessage(System.LastMessageText)
    - kind: InvokeAzureAgent
      id: call_prompt_agent
      agent:
        name: {promptAgentName}
      conversationId: =System.ConversationId
      input:
        messages: =Local.UserInput
      output:
        messages: Local.AgentResponse
    - kind: SendActivity
      id: completion_message
      activity: ✅ ワークフロー完了！
    - kind: EndConversation
      id: end_conversation
";
    }
}

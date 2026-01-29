// Telemetry Service
// ==================
// OpenTelemetry + Application Insights integration

using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FoundryControlPlane.Telemetry;

/// <summary>
/// OpenTelemetry と Application Insights を統合したテレメトリサービス
/// </summary>
public class TelemetryService : IDisposable
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly IConfiguration _configuration;
    private TracerProvider? _tracerProvider;
    private bool _disposed;

    private static readonly ActivitySource ActivitySource = new("FoundryControlPlane", "1.0.0");

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// テレメトリを初期化
    /// </summary>
    public Task InitializeAsync()
    {
        var connectionString = _configuration["ApplicationInsights:ConnectionString"];

        var builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService("FoundryControlPlane", serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development"
                }))
            .AddSource(ActivitySource.Name)
            .AddHttpClientInstrumentation();

        // Application Insights が設定されている場合は追加
        if (!string.IsNullOrEmpty(connectionString))
        {
            builder.AddAzureMonitorTraceExporter(options =>
            {
                options.ConnectionString = connectionString;
            });
            _logger.LogInformation("Application Insights テレメトリを有効化しました");
        }
        else
        {
            // Application Insights が設定されていない場合は警告のみ（開発環境向け）
            _logger.LogWarning("Application Insights が設定されていません。ログ出力のみを使用します");
        }

        _tracerProvider = builder.Build();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 新しいアクティビティ (スパン) を開始
    /// </summary>
    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind);
    }

    /// <summary>
    /// エージェント操作のトレース
    /// </summary>
    public Activity? TraceAgentOperation(string operationType, string agentName)
    {
        var activity = ActivitySource.StartActivity($"Agent.{operationType}", ActivityKind.Client);
        activity?.SetTag("agent.name", agentName);
        activity?.SetTag("agent.operation", operationType);
        return activity;
    }

    /// <summary>
    /// GroupChat セッションのトレース
    /// </summary>
    public Activity? TraceGroupChatSession(string sessionId, IEnumerable<string> participants)
    {
        var activity = ActivitySource.StartActivity("GroupChat.Session", ActivityKind.Internal);
        activity?.SetTag("groupchat.session_id", sessionId);
        activity?.SetTag("groupchat.participants", string.Join(", ", participants));
        return activity;
    }

    /// <summary>
    /// メッセージ交換のトレース
    /// </summary>
    public Activity? TraceMessageExchange(string fromAgent, string toAgent, string messageType)
    {
        var activity = ActivitySource.StartActivity("Agent.MessageExchange", ActivityKind.Producer);
        activity?.SetTag("message.from", fromAgent);
        activity?.SetTag("message.to", toAgent);
        activity?.SetTag("message.type", messageType);
        return activity;
    }

    /// <summary>
    /// トークン使用量を記録
    /// </summary>
    public void RecordTokenUsage(Activity? activity, int promptTokens, int completionTokens)
    {
        activity?.SetTag("tokens.prompt", promptTokens);
        activity?.SetTag("tokens.completion", completionTokens);
        activity?.SetTag("tokens.total", promptTokens + completionTokens);
    }

    /// <summary>
    /// エラーを記録
    /// </summary>
    public void RecordError(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _tracerProvider?.Dispose();
        _disposed = true;
    }
}

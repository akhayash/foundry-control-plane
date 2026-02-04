using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace CustomAgent.Agents;

internal sealed class A2AChatAgent
{
    private const string SystemPrompt = "You are a concise helper for short answers.";

    private readonly ChatClient _chatClient;
    private readonly ILogger<A2AChatAgent> _logger;

    public A2AChatAgent(AzureOpenAIClient client, IOptions<OpenAIOptions> options, ILogger<A2AChatAgent> logger)
    {
        _chatClient = client.GetChatClient(options.Value.DeploymentName);
        _logger = logger;
    }

    public void Attach(TaskManager taskManager, string agentUrl)
    {
        taskManager.OnMessageReceived = (message, cancellationToken) => ProcessMessageAsync(message, cancellationToken);
        taskManager.OnAgentCardQuery = (_, cancellationToken) => Task.FromResult(GetAgentCard(agentUrl));
    }

    public AgentCard GetAgentCard(string agentUrl)
    {
        return new AgentCard
        {
            Name = "A2A Chat Agent",
            Description = "Uses Azure OpenAI to answer chat requests.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = new List<string> { "text" },
            DefaultOutputModes = new List<string> { "text" },
            Capabilities = new AgentCapabilities
            {
                Streaming = false
            }
        };
    }

    private async Task<A2AResponse> ProcessMessageAsync(MessageSendParams sendParams, CancellationToken cancellationToken)
    {
        var userText = sendParams.Message.Parts.OfType<TextPart>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(userText))
        {
            return BuildAgentMessage(sendParams, "I did not receive any text to process.");
        }

        try
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(SystemPrompt),
                ChatMessage.CreateUserMessage(userText)
            };

            var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var chatCompletion = response.Value;

            var completion = chatCompletion.Content.FirstOrDefault()?.Text;
            var reply = string.IsNullOrWhiteSpace(completion)
                ? "I could not generate a response."
                : completion;

            return BuildAgentMessage(sendParams, reply.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A2A chat handler failed to process message.");
            return BuildAgentMessage(sendParams, "Something went wrong while generating a response.");
        }
    }

    private static AgentMessage BuildAgentMessage(MessageSendParams sendParams, string text)
    {
        return new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = sendParams.Message.ContextId,
            Parts = new List<Part> { new TextPart { Text = text } }
        };
    }
}

using ElRucio.Platform.Commanding;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Orchestration;

public sealed class InboundChatProcessor(
    ChatCommandService commandService,
    ChatOrchestrator orchestrator,
    IOptions<ElRucioOptions> appOptions,
    ILogger<InboundChatProcessor> logger) : IInboundChatProcessor
{
    private readonly ElRucioOptions _app = appOptions.Value;

    public async Task<bool> ProcessAsync(
        InboundMessage message,
        Func<ConversationRef, string, CancellationToken, Task> sendText,
        CancellationToken cancellationToken)
    {
        var conversation = message.Conversation;
        if (!IsAllowed(conversation))
        {
            logger.LogInformation("Ignoring message from non-allowlisted conversation {Conversation}", conversation.ChatKey);
            return false;
        }

        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (await commandService.TryHandleAsync(conversation, text, sendText, cancellationToken))
        {
            return true;
        }

        var response = await orchestrator.HandleUserPromptAsync(conversation.ChatKey, text, cancellationToken);
        await sendText(conversation, response, cancellationToken);
        return true;
    }

    private bool IsAllowed(ConversationRef conversation)
        => _app.AllowedChatIds.Count == 0
           || _app.AllowedChatIds.Contains(conversation.ChatKey)
           || _app.AllowedChatIds.Contains(conversation.ConversationId);
}

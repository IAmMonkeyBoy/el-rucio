using ElRucio.Shared.Models;

namespace ElRucio.Shared.Contracts;

public interface IAgentRuntime
{
    Task<string> SendPromptAsync(string chatId, string prompt, CancellationToken cancellationToken);

    Task<string> SendPromptAsync(ConversationRef conversation, string prompt, CancellationToken cancellationToken)
        => SendPromptAsync(conversation.ChatKey, prompt, cancellationToken);
}

public interface ISessionStore
{
    Task<SessionBinding?> GetAsync(string chatId, CancellationToken cancellationToken);
    Task UpsertAsync(SessionBinding binding, CancellationToken cancellationToken);
    Task DeleteAsync(string chatId, CancellationToken cancellationToken);

    Task<SessionBinding?> GetAsync(ConversationRef conversation, CancellationToken cancellationToken)
        => GetAsync(conversation.ChatKey, cancellationToken);

    Task DeleteAsync(ConversationRef conversation, CancellationToken cancellationToken)
        => DeleteAsync(conversation.ChatKey, cancellationToken);
}

public interface IMemoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<string> BuildMemoryContextBlockAsync(string chatId, CancellationToken cancellationToken);
    Task SaveTurnAsync(string chatId, string sessionId, string role, string content, CancellationToken cancellationToken);
    Task ApplyDailyDecayAsync(CancellationToken cancellationToken);

    Task<string> BuildMemoryContextBlockAsync(ConversationRef conversation, CancellationToken cancellationToken)
        => BuildMemoryContextBlockAsync(conversation.ChatKey, cancellationToken);

    Task SaveTurnAsync(ConversationRef conversation, string sessionId, string role, string content, CancellationToken cancellationToken)
        => SaveTurnAsync(conversation.ChatKey, sessionId, role, content, cancellationToken);
}

public interface IApprovalStore
{
    Task QueueAsync(ApprovalRequest request, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetPendingAsync(string approvalId, string chatId, CancellationToken cancellationToken);
    Task SetStatusAsync(string approvalId, string status, CancellationToken cancellationToken);

    Task<ApprovalRequest?> GetPendingAsync(string approvalId, ConversationRef conversation, CancellationToken cancellationToken)
        => GetPendingAsync(approvalId, conversation.ChatKey, cancellationToken);
}

public interface IScheduledTaskStore
{
    Task<List<ScheduledTaskItem>> ListAsync(string chatId, CancellationToken cancellationToken);
    Task InsertAsync(ScheduledTaskItem item, CancellationToken cancellationToken);
    Task<List<ScheduledTaskItem>> GetDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task SetEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken);
    Task DeleteAsync(string taskId, CancellationToken cancellationToken);
    Task TouchRunAsync(string taskId, DateTimeOffset? lastRunUtc, DateTimeOffset nextRunUtc, CancellationToken cancellationToken);

    Task<List<ScheduledTaskItem>> ListAsync(ConversationRef conversation, CancellationToken cancellationToken)
        => ListAsync(conversation.ChatKey, cancellationToken);
}

public interface IOutboundMessenger
{
    Task SendTextAsync(string chatId, string text, CancellationToken cancellationToken);

    Task SendTextAsync(ConversationRef conversation, string text, CancellationToken cancellationToken)
        => SendTextAsync(conversation.ChatKey, text, cancellationToken);

    Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
        => SendTextAsync(message.Conversation, message.Text, cancellationToken);
}

public interface IVoiceTranscriber
{
    Task<string?> TranscribeAsync(string filePath, CancellationToken cancellationToken);
}

public interface IVideoAnalyzer
{
    Task<string> AnalyzeAsync(string filePath, CancellationToken cancellationToken);
}

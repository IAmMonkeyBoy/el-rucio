using ElRucio.Shared.Models;

namespace ElRucio.Shared.Contracts;

public interface IAgentRuntime
{
    Task<string> SendPromptAsync(string chatId, string prompt, CancellationToken cancellationToken);
}

public interface ISessionStore
{
    Task<SessionBinding?> GetAsync(string chatId, CancellationToken cancellationToken);
    Task UpsertAsync(SessionBinding binding, CancellationToken cancellationToken);
    Task DeleteAsync(string chatId, CancellationToken cancellationToken);
}

public interface IMemoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<string> BuildMemoryContextBlockAsync(string chatId, CancellationToken cancellationToken);
    Task SaveTurnAsync(string chatId, string sessionId, string role, string content, CancellationToken cancellationToken);
    Task ApplyDailyDecayAsync(CancellationToken cancellationToken);
}

public interface IApprovalStore
{
    Task QueueAsync(ApprovalRequest request, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetPendingAsync(string approvalId, string chatId, CancellationToken cancellationToken);
    Task SetStatusAsync(string approvalId, string status, CancellationToken cancellationToken);
}

public interface IScheduledTaskStore
{
    Task<List<ScheduledTaskItem>> ListAsync(string chatId, CancellationToken cancellationToken);
    Task InsertAsync(ScheduledTaskItem item, CancellationToken cancellationToken);
    Task<List<ScheduledTaskItem>> GetDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task SetEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken);
    Task DeleteAsync(string taskId, CancellationToken cancellationToken);
    Task TouchRunAsync(string taskId, DateTimeOffset? lastRunUtc, DateTimeOffset nextRunUtc, CancellationToken cancellationToken);
}

public interface IOutboundMessenger
{
    Task SendTextAsync(string chatId, string text, CancellationToken cancellationToken);
}

public interface IVoiceTranscriber
{
    Task<string?> TranscribeAsync(string filePath, CancellationToken cancellationToken);
}

public interface IVideoAnalyzer
{
    Task<string> AnalyzeAsync(string filePath, CancellationToken cancellationToken);
}

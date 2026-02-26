namespace ElRucio.Shared.Models;

public sealed record AttachmentRef(string Kind, string LocalPath, string? MimeType = null);

public sealed record InboundMessage(
    string ChatId,
    string? Text,
    List<AttachmentRef> Attachments,
    DateTimeOffset ReceivedUtc,
    bool IsVoice = false,
    string? VoiceFilePath = null,
    bool VoiceReplyEnabled = false);

public sealed record OutboundMessage(string ChatId, string Text);

public sealed record SessionBinding(string ChatId, string SessionId, DateTimeOffset CreatedUtc, DateTimeOffset LastActiveUtc);

public sealed record MemoryEntry(
    long Id,
    string ChatId,
    string SessionId,
    string Sector,
    string Role,
    string Content,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastAccessUtc,
    double Salience,
    string? MetaJson);

public sealed record ApprovalRequest(
    string Id,
    string ChatId,
    string SessionId,
    string Kind,
    string PayloadJson,
    string Status,
    DateTimeOffset CreatedUtc);

public sealed record ScheduledTaskItem(
    string Id,
    string ChatId,
    string SessionId,
    string Cron,
    string Prompt,
    bool Enabled,
    DateTimeOffset NextRunUtc,
    DateTimeOffset? LastRunUtc,
    DateTimeOffset CreatedUtc);

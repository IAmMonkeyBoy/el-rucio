using System.Text.Json;

namespace ElRucio.Platform.Slack;

internal enum SlackSocketActionType
{
    None,
    Disconnect,
    InboundMessage,
    SlashCommand
}

internal sealed record SlackInboundPayload(
    string ChannelId,
    string? ThreadTs,
    string UserId,
    string Text,
    IReadOnlyList<SlackIncomingFile> Files);

internal sealed record SlackSlashPayload(
    string ChannelId,
    string UserId,
    string Command,
    string Text);

internal sealed record SlackSocketEnvelopeAction(
    string? EnvelopeId,
    string? EnvelopeType,
    string DedupKey,
    SlackSocketActionType ActionType,
    SlackInboundPayload? Inbound,
    SlackSlashPayload? Slash);

internal static class SlackSocketEnvelopeParser
{
    public static SlackSocketEnvelopeAction? Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var envelopeId = GetString(root, "envelope_id");
        var envelopeType = GetString(root, "type");
        var dedupKey = BuildDedupKey(envelopeType, envelopeId, root);

        if (string.Equals(envelopeType, "disconnect", StringComparison.OrdinalIgnoreCase))
        {
            return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.Disconnect, null, null);
        }

        if (string.Equals(envelopeType, "events_api", StringComparison.OrdinalIgnoreCase))
        {
            if (!root.TryGetProperty("payload", out var eventsPayload)
                || !eventsPayload.TryGetProperty("event", out var evt))
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            var type = GetString(evt, "type");
            if (type != "app_mention" && type != "message")
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            var subtype = GetString(evt, "subtype");
            var isFileShare = string.Equals(subtype, "file_share", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(GetString(evt, "bot_id")) || (!string.IsNullOrWhiteSpace(subtype) && !isFileShare))
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            var channelId = GetString(evt, "channel") ?? GetNestedString(evt, "message", "channel");
            var userId = GetString(evt, "user") ?? GetNestedString(evt, "message", "user");
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(userId))
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            var channelType = GetString(evt, "channel_type");
            var isDirectMessage = string.Equals(channelType, "im", StringComparison.OrdinalIgnoreCase);
            var isMention = string.Equals(type, "app_mention", StringComparison.OrdinalIgnoreCase);
            var inferredDirectFromChannel = channelId.StartsWith("D", StringComparison.OrdinalIgnoreCase);
            if (!isDirectMessage && !isMention && !(isFileShare && inferredDirectFromChannel))
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            var text = GetString(evt, "text") ?? GetNestedString(evt, "message", "text") ?? string.Empty;
            var threadTs = GetString(evt, "thread_ts") ?? GetNestedString(evt, "message", "thread_ts");
            if (isMention && string.IsNullOrWhiteSpace(threadTs))
            {
                threadTs = GetString(evt, "ts") ?? GetNestedString(evt, "message", "ts");
            }

            var files = ExtractFiles(evt);
            return new SlackSocketEnvelopeAction(
                envelopeId,
                envelopeType,
                dedupKey,
                SlackSocketActionType.InboundMessage,
                new SlackInboundPayload(channelId, threadTs, userId, text, files),
                null);
        }

        if (string.Equals(envelopeType, "slash_commands", StringComparison.OrdinalIgnoreCase))
        {
            if (!root.TryGetProperty("payload", out var slashPayload))
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            var command = GetString(slashPayload, "command");
            var text = GetString(slashPayload, "text") ?? string.Empty;
            var channelId = GetString(slashPayload, "channel_id");
            var userId = GetString(slashPayload, "user_id");

            if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(userId))
            {
                return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
            }

            return new SlackSocketEnvelopeAction(
                envelopeId,
                envelopeType,
                dedupKey,
                SlackSocketActionType.SlashCommand,
                null,
                new SlackSlashPayload(channelId, userId, command, text));
        }

        return new SlackSocketEnvelopeAction(envelopeId, envelopeType, dedupKey, SlackSocketActionType.None, null, null);
    }

    private static string BuildDedupKey(string? envelopeType, string? envelopeId, JsonElement root)
    {
        if (root.TryGetProperty("payload", out var payload))
        {
            return SlackDedupKeyBuilder.Build(envelopeType, envelopeId, payload);
        }

        using var empty = JsonDocument.Parse("{}");
        return SlackDedupKeyBuilder.Build(envelopeType, envelopeId, empty.RootElement);
    }

    private static List<SlackIncomingFile> ExtractFiles(JsonElement evt)
    {
        var files = new List<SlackIncomingFile>();
        if (evt.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
        {
            AppendFiles(filesElement, files);
        }

        if (evt.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.Object
            && messageElement.TryGetProperty("files", out var nestedFiles)
            && nestedFiles.ValueKind == JsonValueKind.Array)
        {
            AppendFiles(nestedFiles, files);
        }

        return files;
    }

    private static void AppendFiles(JsonElement filesElement, List<SlackIncomingFile> files)
    {
        foreach (var file in filesElement.EnumerateArray())
        {
            var id = GetString(file, "id") ?? Guid.NewGuid().ToString("N");
            var name = GetString(file, "name") ?? id;
            var mimeType = GetString(file, "mimetype");
            var url = GetString(file, "url_private_download") ?? GetString(file, "url_private");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            files.Add(new SlackIncomingFile(id, name, mimeType, url));
        }
    }

    private static string? GetNestedString(JsonElement element, string childName, string name)
    {
        if (!element.TryGetProperty(childName, out var child) || child.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(child, name);
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

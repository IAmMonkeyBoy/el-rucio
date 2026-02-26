using System.Collections.Concurrent;
using System.Text.Json;

namespace ElRucio.Platform.Slack;

internal static class SlackDedupKeyBuilder
{
    public static string Build(string? envelopeType, string? envelopeId, JsonElement payload)
    {
        if (string.Equals(envelopeType, "events_api", StringComparison.OrdinalIgnoreCase))
        {
            var eventId = GetString(payload, "event_id");
            if (!string.IsNullOrWhiteSpace(eventId))
            {
                return $"events:{eventId}";
            }
        }

        if (string.Equals(envelopeType, "slash_commands", StringComparison.OrdinalIgnoreCase))
        {
            var triggerId = GetString(payload, "trigger_id");
            if (!string.IsNullOrWhiteSpace(triggerId))
            {
                return $"slash:{triggerId}";
            }

            var command = GetString(payload, "command") ?? string.Empty;
            var channelId = GetString(payload, "channel_id") ?? string.Empty;
            var userId = GetString(payload, "user_id") ?? string.Empty;
            var text = GetString(payload, "text") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(command) || !string.IsNullOrWhiteSpace(channelId))
            {
                return $"slash-fallback:{command}:{channelId}:{userId}:{text}";
            }
        }

        return $"envelope:{envelopeType ?? "unknown"}:{envelopeId ?? Guid.NewGuid().ToString("N")}";
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

internal sealed class SlackDedupState
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedEventKeys = new(StringComparer.Ordinal);
    private readonly int _windowSeconds;

    public SlackDedupState(int windowSeconds)
    {
        _windowSeconds = Math.Max(30, windowSeconds);
    }

    public bool TryBeginProcessing(string dedupKey)
    {
        CleanupExpired();

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddSeconds(_windowSeconds);

        while (true)
        {
            if (_processedEventKeys.TryGetValue(dedupKey, out var existingExpiry))
            {
                if (existingExpiry > now)
                {
                    return false;
                }

                if (_processedEventKeys.TryUpdate(dedupKey, expires, existingExpiry))
                {
                    return true;
                }

                continue;
            }

            if (_processedEventKeys.TryAdd(dedupKey, expires))
            {
                return true;
            }
        }
    }

    public void EndProcessingWithFailure(string dedupKey)
    {
        _processedEventKeys.TryRemove(dedupKey, out _);
    }

    public void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _processedEventKeys)
        {
            if (pair.Value <= now)
            {
                _processedEventKeys.TryRemove(pair.Key, out _);
            }
        }
    }
}

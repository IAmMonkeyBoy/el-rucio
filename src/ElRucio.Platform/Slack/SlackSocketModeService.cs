using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using ElRucio.Platform.Commanding;
using ElRucio.Platform.Orchestration;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Slack;

public sealed class SlackSocketModeService(
    SlackApiClient slackApiClient,
    ChatCommandService commandService,
    ChatOrchestrator orchestrator,
    IVoiceTranscriber voiceTranscriber,
    IVideoAnalyzer videoAnalyzer,
    IOptions<ElRucioOptions> appOptions,
    IOptions<SlackOptions> slackOptions,
    IOptions<VoiceOptions> voiceOptions,
    IOptions<VideoOptions> videoOptions,
    ILogger<SlackSocketModeService> logger) : BackgroundService, IOutboundMessenger
{
    private readonly ElRucioOptions _app = appOptions.Value;
    private readonly SlackOptions _slack = slackOptions.Value;
    private readonly VoiceOptions _voice = voiceOptions.Value;
    private readonly VideoOptions _video = videoOptions.Value;
    private readonly SlackDedupState _dedupState = new(slackOptions.Value.DedupWindowSeconds);
    private readonly ConcurrentDictionary<string, (string Analysis, DateTimeOffset CreatedUtc)> _recentMediaAnalyses = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var socketUrl = await slackApiClient.OpenSocketConnectionAsync(stoppingToken);
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(socketUrl, stoppingToken);
                logger.LogInformation("Slack Socket Mode connected");

                await ReceiveLoopAsync(socket, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Slack Socket Mode loop error; reconnecting");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    public async Task SendTextAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var channelId = chatId.StartsWith("slack:", StringComparison.OrdinalIgnoreCase)
            ? chatId["slack:".Length..]
            : chatId;

        foreach (var chunk in SplitForSlack(text, _app.MaxResponseChars))
        {
            await slackApiClient.SendMessageAsync(channelId, chunk, null, cancellationToken);
        }
    }

    public async Task SendTextAsync(ConversationRef conversation, string text, CancellationToken cancellationToken)
    {
        foreach (var chunk in SplitForSlack(text, _app.MaxResponseChars))
        {
            await slackApiClient.SendMessageAsync(conversation.ConversationId, chunk, conversation.ThreadId, cancellationToken);
        }
    }

    public async Task HandleInboundTextAsync(string channelId, string? threadTs, string userId, string rawText, IReadOnlyList<SlackIncomingFile>? files, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCommandText(rawText);
        var conversation = new ConversationRef("slack", channelId, threadTs, userId);

        if (!IsAllowed(conversation.ChatKey))
        {
            logger.LogInformation("Ignoring Slack message from non-allowlisted conversation {Conversation}", conversation.ChatKey);
            return;
        }

        if (await commandService.TryHandleAsync(conversation, normalized, SendTextAsync, cancellationToken))
        {
            return;
        }

        var mediaAnalysis = await TryAnalyzeMediaAsync(conversation, files, cancellationToken);
        if (!string.IsNullOrWhiteSpace(mediaAnalysis) && !IsMediaAnalysisFailure(mediaAnalysis))
        {
            _recentMediaAnalyses[BuildMediaCacheKey(conversation)] = (mediaAnalysis, DateTimeOffset.UtcNow);
        }

        if (string.IsNullOrWhiteSpace(mediaAnalysis)
            && _recentMediaAnalyses.TryGetValue(BuildMediaCacheKey(conversation), out var cached)
            && (DateTimeOffset.UtcNow - cached.CreatedUtc) <= TimeSpan.FromMinutes(10))
        {
            mediaAnalysis = cached.Analysis;
        }

        var voiceText = await TryTranscribeVoiceAsync(conversation, files, cancellationToken);
        if (files is { Count: > 0 }
            && string.IsNullOrWhiteSpace(mediaAnalysis)
            && string.IsNullOrWhiteSpace(voiceText))
        {
            logger.LogWarning("Slack media was attached but no analysis/transcript was produced. Conversation={ConversationId}, files={Count}", conversation.ConversationId, files.Count);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                await SendTextAsync(conversation, "I received your media but couldn’t analyze it yet. Please verify Slack scope `files:read`, then resend the media.", cancellationToken);
                return;
            }

            normalized = $"{normalized}\n\n[Media note]\nMedia was attached but analysis was unavailable.";
        }

        if (!string.IsNullOrWhiteSpace(voiceText))
        {
            normalized = string.IsNullOrWhiteSpace(normalized)
                ? $"[Voice transcribed]: {voiceText}"
                : $"{normalized}\n\n[Voice transcribed]: {voiceText}";
        }

        if (!string.IsNullOrWhiteSpace(mediaAnalysis))
        {
            var mediaContext = $"[Media analysis]\n{mediaAnalysis}\n\n[Instruction]\nUse the media analysis above as the source of truth. Do not claim you cannot see or access the media. Respond using that analysis.";
            normalized = string.IsNullOrWhiteSpace(normalized)
                ? $"{mediaContext}\n\nProvide a concise summary and practical feedback."
                : $"{normalized}\n\n{mediaContext}";
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            await SendTextAsync(conversation, "Send text or attach voice/media files if enabled.", cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(mediaAnalysis)
            && files is { Count: > 0 }
            && IsLikelySlackAutoMediaCaption(rawText))
        {
            await SendTextAsync(conversation, $"I analyzed your media successfully.\n\n{mediaAnalysis}", cancellationToken);
            return;
        }

        var response = await orchestrator.HandleUserPromptAsync(conversation.ChatKey, normalized, cancellationToken);
        if (!string.IsNullOrWhiteSpace(mediaAnalysis) && LooksLikeMediaAccessOrGenericFallback(response))
        {
            response = $"I analyzed your media successfully.\n\n{mediaAnalysis}";
        }

        await SendTextAsync(conversation, response, cancellationToken);
    }

    private bool IsAllowed(string chatKey)
        => _app.AllowedChatIds.Count == 0 || _app.AllowedChatIds.Contains(chatKey);

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var payload = await ReceiveMessageAsync(socket, buffer, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            var parsed = SlackSocketEnvelopeParser.Parse(payload);
            if (parsed is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(parsed.EnvelopeId))
            {
                await AckEnvelopeAsync(socket, parsed.EnvelopeId, cancellationToken);
            }

            switch (parsed.ActionType)
            {
                case SlackSocketActionType.InboundMessage when parsed.Inbound is not null:
                    if (_dedupState.TryBeginProcessing(parsed.DedupKey))
                    {
                        try
                        {
                            await HandleInboundTextAsync(
                                parsed.Inbound.ChannelId,
                                parsed.Inbound.ThreadTs,
                                parsed.Inbound.UserId,
                                parsed.Inbound.Text,
                                parsed.Inbound.Files,
                                cancellationToken);
                        }
                        catch
                        {
                            _dedupState.EndProcessingWithFailure(parsed.DedupKey);
                            throw;
                        }
                    }
                    break;
                case SlackSocketActionType.SlashCommand when parsed.Slash is not null:
                    if (_dedupState.TryBeginProcessing(parsed.DedupKey))
                    {
                        try
                        {
                            var commandText = string.IsNullOrWhiteSpace(parsed.Slash.Text)
                                ? parsed.Slash.Command
                                : $"{parsed.Slash.Command} {parsed.Slash.Text}";

                            await HandleInboundTextAsync(
                                parsed.Slash.ChannelId,
                                null,
                                parsed.Slash.UserId,
                                commandText,
                                null,
                                cancellationToken);
                        }
                        catch
                        {
                            _dedupState.EndProcessingWithFailure(parsed.DedupKey);
                            throw;
                        }
                    }
                    break;
                case SlackSocketActionType.Disconnect:
                    logger.LogWarning("Slack requested socket disconnect");
                    return;
                case SlackSocketActionType.None:
                    break;
            }
        }
    }

    private static async Task AckEnvelopeAsync(ClientWebSocket socket, string envelopeId, CancellationToken cancellationToken)
    {
        var ack = JsonSerializer.Serialize(new Dictionary<string, string> { ["envelope_id"] = envelopeId });
        var bytes = Encoding.UTF8.GetBytes(ack);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string?> ReceiveMessageAsync(ClientWebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var segment = new ArraySegment<byte>(buffer);
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(segment, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IEnumerable<string> SplitForSlack(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var chunkSize = Math.Max(256, Math.Min(maxChars, 3500));
        for (var offset = 0; offset < text.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, text.Length - offset);
            yield return text.Substring(offset, length);
        }
    }

    private async Task<string?> TryTranscribeVoiceAsync(
        ConversationRef conversation,
        IReadOnlyList<SlackIncomingFile>? files,
        CancellationToken cancellationToken)
    {
        if (!_voice.Enabled || files is null || files.Count == 0)
        {
            return null;
        }

        var audio = files.FirstOrDefault(IsAudioCandidate);
        if (audio is null)
        {
            return null;
        }

        var root = Path.Combine(_app.DataDir, "media", "slack", conversation.ConversationId);
        var downloaded = await slackApiClient.DownloadFileAsync(audio.DownloadUrl, root, cancellationToken);
        if (string.IsNullOrWhiteSpace(downloaded))
        {
            return null;
        }

        try
        {
            return await voiceTranscriber.TranscribeAsync(downloaded, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slack audio transcription failed");
            return null;
        }
    }

    private async Task<string?> TryAnalyzeMediaAsync(
        ConversationRef conversation,
        IReadOnlyList<SlackIncomingFile>? files,
        CancellationToken cancellationToken)
    {
        if (!_video.Enabled || files is null || files.Count == 0)
        {
            return null;
        }

        var media = files.FirstOrDefault(IsVisualMediaCandidate);

        if (media is null)
        {
            logger.LogInformation("Slack message had files but none matched visual media criteria. Files={Count}", files.Count);
            return null;
        }

        logger.LogInformation("Slack visual media candidate selected: name={Name}, mime={MimeType}", media.Name, media.MimeType ?? "(none)");

        var root = Path.Combine(_app.DataDir, "media", "slack", conversation.ConversationId);
        var downloaded = await slackApiClient.DownloadFileAsync(media.DownloadUrl, root, cancellationToken);
        if (string.IsNullOrWhiteSpace(downloaded))
        {
            return null;
        }

        try
        {
            return await videoAnalyzer.AnalyzeAsync(downloaded, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Slack media analysis provider not fully wired");
            return "Media analysis is enabled but provider wiring is incomplete.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slack media analysis failed");
            return "Media analysis failed; continuing without it.";
        }
    }

    private static bool IsAudioCandidate(SlackIncomingFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.MimeType) && file.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        return ext is ".wav" or ".mp3" or ".m4a" or ".ogg" or ".oga" or ".webm";
    }

    private static bool IsVisualMediaCandidate(SlackIncomingFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.MimeType)
            && (file.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || file.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"
            or ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi";
    }

    private string NormalizeCommandText(string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith('/'))
        {
            return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(_slack.BotUserId))
        {
            var mention = $"<@{_slack.BotUserId}>";
            if (trimmed.StartsWith(mention, StringComparison.OrdinalIgnoreCase))
            {
                var body = trimmed[mention.Length..].TrimStart();
                if (body.StartsWith('/'))
                {
                    return body;
                }

                var parts = body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var command = "/" + parts[0].ToLowerInvariant();
                    return parts.Length == 1 ? command : $"{command} {parts[1]}";
                }
            }
        }

        return trimmed;
    }

    private static string BuildMediaCacheKey(ConversationRef conversation)
        => $"{conversation.Provider}:{conversation.ConversationId}:{conversation.ThreadId ?? "root"}";

    private static bool IsMediaAnalysisFailure(string analysis)
    {
        if (string.IsNullOrWhiteSpace(analysis))
        {
            return true;
        }

        return analysis.StartsWith("Image analysis failed", StringComparison.OrdinalIgnoreCase)
               || analysis.StartsWith("Video analysis failed", StringComparison.OrdinalIgnoreCase)
               || analysis.StartsWith("Video summary failed", StringComparison.OrdinalIgnoreCase)
               || analysis.StartsWith("Unsupported media type", StringComparison.OrdinalIgnoreCase)
               || analysis.StartsWith("Video analysis could not extract", StringComparison.OrdinalIgnoreCase)
               || analysis.StartsWith("Video analysis requires ffmpeg", StringComparison.OrdinalIgnoreCase)
               || analysis.StartsWith("Video/image analysis key not configured", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMediaAccessOrGenericFallback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        var directDisclaimer = (lower.Contains("can't see") || lower.Contains("cannot see") || lower.Contains("don’t see") || lower.Contains("don't see"))
                              && (lower.Contains("image") || lower.Contains("video") || lower.Contains("media") || lower.Contains("visual"));

        if (directDisclaimer)
        {
            return true;
        }

        return lower.Contains("without direct visual processing")
               || lower.Contains("without direct visual access")
               || lower.Contains("user-supplied context")
               || lower.Contains("share detailed descriptions")
               || lower.Contains("provide a detailed description")
               || lower.Contains("need for user-supplied")
               || lower.Contains("limitation") && (lower.Contains("visual") || lower.Contains("media"));
    }

    private static bool IsLikelySlackAutoMediaCaption(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim().ToLowerInvariant();
        if (trimmed is "video" or "video 1x" or "video 1" or "image" or "image 1x" or "image 1")
        {
            return true;
        }

        return (trimmed.StartsWith("video ") || trimmed.StartsWith("image "))
               && (trimmed.EndsWith("x") || trimmed.Contains(" 1x"));
    }
}

public sealed record SlackIncomingFile(string Id, string Name, string? MimeType, string DownloadUrl);

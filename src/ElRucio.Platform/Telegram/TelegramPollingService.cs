using Cronos;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ElRucio.Memory.Sqlite;
using ElRucio.Platform.Commanding;
using ElRucio.Platform.Orchestration;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Telegram;

public sealed class TelegramPollingService(
    TelegramApiClient apiClient,
    ChatCommandService commandService,
    ChatOrchestrator orchestrator,
    SqliteDb sqliteDb,
    ISessionStore sessionStore,
    IScheduledTaskStore scheduledTaskStore,
    IVoiceTranscriber voiceTranscriber,
    IVideoAnalyzer videoAnalyzer,
    IOptions<ElRucioOptions> appOptions,
    IOptions<VoiceOptions> voiceOptions,
    IOptions<VideoOptions> videoOptions,
    ILogger<TelegramPollingService> logger) : BackgroundService, IOutboundMessenger
{
    private static readonly Regex NaturalScheduleRegex = new(
        @"schedule(?:\s+a\s+task)?\s+(?:for|in)\s+(?<mins>\d+)\s+min(?:ute)?s?(?:\s+from\s+now)?\s+to\s+(?<prompt>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ElRucioOptions _app = appOptions.Value;
    private readonly VoiceOptions _voice = voiceOptions.Value;
    private readonly VideoOptions _video = videoOptions.Value;
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private long _offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await apiClient.GetUpdatesAsync(_offset, stoppingToken);
                foreach (var update in updates)
                {
                    _offset = Math.Max(_offset, update.UpdateId + 1);
                    if (update.Message?.Chat is null)
                    {
                        continue;
                    }

                    var chatId = update.Message.Chat.Id.ToString();
                    if (!IsAllowed(chatId))
                    {
                        continue;
                    }

                    await HandleMessageAsync(chatId, update.Message, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram polling iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    public async Task SendTextAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var html = TelegramHtmlFormatter.ToSafeHtml(text);
        foreach (var chunk in TelegramHtmlFormatter.SplitForTelegram(html, _app.MaxResponseChars))
        {
            await apiClient.SendTextAsync(chatId, chunk, cancellationToken);
        }
    }

    public Task SendTextAsync(ConversationRef conversation, string text, CancellationToken cancellationToken)
        => SendTextAsync(conversation.ConversationId, text, cancellationToken);

    private bool IsAllowed(string chatId)
        => _app.AllowedChatIds.Count == 0 || _app.AllowedChatIds.Contains(chatId);

    private async Task HandleMessageAsync(string chatId, TelegramMessage message, CancellationToken cancellationToken)
    {
        var text = message.Text?.Trim();
        var conversation = ConversationRef.Telegram(chatId);
        var mediaAnalysis = await HandleMediaAnalysisAsync(chatId, message, cancellationToken);

        if (!string.IsNullOrWhiteSpace(text) && text.StartsWith('/'))
        {
            await commandService.TryHandleAsync(conversation, text, SendTextAsync, cancellationToken);
            return;
        }

        if (message.Voice is not null && _voice.Enabled)
        {
            var voiceText = await HandleVoiceAsync(chatId, message.Voice.FileId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(voiceText))
            {
                text = $"[Voice transcribed]: {voiceText}";
            }
        }

        if (!string.IsNullOrWhiteSpace(mediaAnalysis))
        {
            text = string.IsNullOrWhiteSpace(text)
                ? $"[Media analysis]\n{mediaAnalysis}\n\nPlease help me with this media."
                : $"{text}\n\n[Media analysis]\n{mediaAnalysis}";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            await SendTextAsync(chatId, "Send text, voice, or media (photo/video) if enabled.", cancellationToken);
            return;
        }

        if (await commandService.TryHandleAsync(conversation, text, SendTextAsync, cancellationToken))
        {
            return;
        }

        var response = await orchestrator.HandleUserPromptAsync(chatId, text, cancellationToken);
        await SendTextAsync(chatId, response, cancellationToken);
    }

    private async Task HandleCommandAsync(string chatId, string command, CancellationToken cancellationToken)
    {
        var parts = command.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var rawHead = parts[0].ToLowerInvariant();
        var head = rawHead.Split('@', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        switch (head)
        {
            case "/start":
                await SendTextAsync(chatId, "El Rucio connected.", cancellationToken);
                break;
            case "/status":
                await SendTextAsync(chatId, await BuildStatusMessageAsync(cancellationToken), cancellationToken);
                break;
            case "/diag":
                await SendTextAsync(chatId, await BuildDiagMessageAsync(cancellationToken), cancellationToken);
                break;
            case "/newchat":
                await sessionStore.DeleteAsync(chatId, cancellationToken);
                await SendTextAsync(chatId, "Started a fresh chat session.", cancellationToken);
                break;
            case "/approve" when parts.Length >= 2:
                await SendTextAsync(chatId, await orchestrator.ApproveAsync(chatId, parts[1], cancellationToken), cancellationToken);
                break;
            case "/cancel" when parts.Length >= 2:
                await SendTextAsync(chatId, await orchestrator.CancelAsync(chatId, parts[1], cancellationToken), cancellationToken);
                break;
            case "/voice" when parts.Length >= 2:
                _voice.ForceVoiceReply = string.Equals(parts[1], "on", StringComparison.OrdinalIgnoreCase);
                await SendTextAsync(chatId, $"Voice reply toggle now: {(_voice.ForceVoiceReply ? "on" : "off")}", cancellationToken);
                break;
            case "/schedule":
                await HandleScheduleCommandAsync(chatId, parts, cancellationToken);
                break;
            default:
                await SendTextAsync(chatId, "Unknown command.", cancellationToken);
                break;
        }
    }

    private async Task HandleScheduleCommandAsync(string chatId, string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            await SendTextAsync(chatId, "Usage: /schedule create|list|pause|resume|delete", cancellationToken);
            return;
        }

        var action = parts[1].ToLowerInvariant();
        if (action == "list")
        {
            var items = await scheduledTaskStore.ListAsync(chatId, cancellationToken);
            var lines = items.Count == 0
                ? "No schedules."
                : string.Join("\n", items.Select(x => $"{x.Id} | {(x.Enabled ? "on" : "off")} | {x.Cron} | next {x.NextRunUtc:O}"));
            await SendTextAsync(chatId, lines, cancellationToken);
            return;
        }

        if (action == "create" && parts.Length >= 3)
        {
            var payload = parts[2];
            var marker = payload.IndexOf('|');
            if (marker <= 0)
            {
                await SendTextAsync(chatId, "Use: /schedule create <cron>|<prompt>", cancellationToken);
                return;
            }

            var cronText = payload[..marker].Trim();
            var prompt = payload[(marker + 1)..].Trim();
            var cron = CronExpression.Parse(cronText);
            var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc) ?? DateTimeOffset.UtcNow.AddMinutes(5);

            var binding = await sessionStore.GetAsync(chatId, cancellationToken)
                          ?? new SessionBinding(chatId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await sessionStore.UpsertAsync(binding, cancellationToken);

            var item = new ScheduledTaskItem(
                Guid.NewGuid().ToString("N")[..8],
                chatId,
                binding.SessionId,
                cronText,
                prompt,
                true,
                next,
                null,
                DateTimeOffset.UtcNow,
                "telegram",
                chatId);

            await scheduledTaskStore.InsertAsync(item, cancellationToken);
            await SendTextAsync(chatId, $"Schedule created: {item.Id}", cancellationToken);
            return;
        }

        if ((action == "pause" || action == "resume" || action == "delete") && parts.Length >= 3)
        {
            var id = parts[2].Trim();
            if (action == "delete")
            {
                await scheduledTaskStore.DeleteAsync(id, cancellationToken);
            }
            else
            {
                await scheduledTaskStore.SetEnabledAsync(id, action == "resume", cancellationToken);
            }

            await SendTextAsync(chatId, $"Schedule {action} complete for {id}", cancellationToken);
            return;
        }

        await SendTextAsync(chatId, "Invalid /schedule command.", cancellationToken);
    }

    private async Task<string?> HandleVoiceAsync(string chatId, string fileId, CancellationToken cancellationToken)
    {
        var root = Path.Combine(_app.DataDir, "media", "telegram", chatId);
        var downloaded = await apiClient.DownloadFileAsync(fileId, root, cancellationToken);
        if (string.IsNullOrWhiteSpace(downloaded))
        {
            return null;
        }

        return await voiceTranscriber.TranscribeAsync(downloaded, cancellationToken);
    }

    private async Task<string> BuildStatusMessageAsync(CancellationToken cancellationToken)
    {
        var ffmpegReady = await IsToolAvailableAsync(_video.FfmpegPath, "-version", cancellationToken);
        var lines = new List<string>
        {
            orchestrator.BuildStatusMessage(),
            $"Voice: enabled={_voice.Enabled}, provider={_voice.SttProvider}, keyConfigured={!string.IsNullOrWhiteSpace(_voice.OpenAiApiKey)}",
            $"Video: enabled={_video.Enabled}, provider={_video.Provider}, keyConfigured={!string.IsNullOrWhiteSpace(_video.ApiKey)}",
            $"Video runtime: model={_video.AnalysisModel}, frameSampleCount={_video.FrameSampleCount}, ffmpegPath={_video.FfmpegPath}, ffmpegReady={ffmpegReady}"
        };

        return string.Join("\n", lines);
    }

    private static async Task<bool> IsToolAvailableAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> HandleMediaAnalysisAsync(string chatId, TelegramMessage message, CancellationToken cancellationToken)
    {
        if (!_video.Enabled)
        {
            return null;
        }

        var fileId = message.Photo?.LastOrDefault()?.FileId ?? message.Video?.FileId;
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        var root = Path.Combine(_app.DataDir, "media", "telegram", chatId);
        var downloaded = await apiClient.DownloadFileAsync(fileId, root, cancellationToken);
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
            logger.LogWarning(ex, "Video analyzer provider not fully wired");
            return "Video analysis is enabled but provider wiring is incomplete.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Media analysis failed");
            return "Media analysis failed; continuing without it.";
        }
    }

    private async Task<string> BuildDiagMessageAsync(CancellationToken cancellationToken)
    {
        var ffmpegReady = await IsToolAvailableAsync(_video.FfmpegPath, "-version", cancellationToken);
        var uptime = DateTimeOffset.UtcNow - _startedUtc;

        var (sessions, memories, approvalsPending, scheduledEnabled, overdueCount, oldestDueAgeMinutes, latestRunUtc) = await ReadDbCountsAsync(cancellationToken);
        var backupAge = ReadLatestBackupAge();

        var lines = new List<string>
        {
            "Diag report",
            $"Uptime: {uptime:dd\\.hh\\:mm\\:ss}",
            $"Voice: enabled={_voice.Enabled}, provider={_voice.SttProvider}, keyConfigured={!string.IsNullOrWhiteSpace(_voice.OpenAiApiKey)}",
            $"Video: enabled={_video.Enabled}, provider={_video.Provider}, model={_video.AnalysisModel}, frames={_video.FrameSampleCount}, ffmpegReady={ffmpegReady}",
            $"DB: sessions={sessions}, memories={memories}, approvalsPending={approvalsPending}, scheduledEnabled={scheduledEnabled}",
            $"Scheduler: overdue={overdueCount}, oldestDueAgeMin={(oldestDueAgeMinutes is null ? "n/a" : oldestDueAgeMinutes.Value.ToString("F1"))}, latestRunUtc={(latestRunUtc ?? "n/a")}",
            $"Backup: {backupAge}"
        };

        return string.Join("\n", lines);
    }

    private async Task<(long sessions, long memories, long approvalsPending, long scheduledEnabled, long overdueCount, double? oldestDueAgeMinutes, string? latestRunUtc)> ReadDbCountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = sqliteDb.Open();
        var overdueCount = await ScalarCountAsync(connection, "SELECT COUNT(*) FROM scheduled_tasks WHERE enabled = 1 AND datetime(next_run_utc) <= datetime('now')", cancellationToken);
        var oldestDueIso = await ScalarStringAsync(connection, "SELECT MIN(next_run_utc) FROM scheduled_tasks WHERE enabled = 1 AND datetime(next_run_utc) <= datetime('now')", cancellationToken);
        var latestRunUtc = await ScalarStringAsync(connection, "SELECT MAX(last_run_utc) FROM scheduled_tasks WHERE last_run_utc IS NOT NULL", cancellationToken);

        double? oldestDueAgeMinutes = null;
        if (!string.IsNullOrWhiteSpace(oldestDueIso) && DateTimeOffset.TryParse(oldestDueIso, out var oldestDue))
        {
            oldestDueAgeMinutes = (DateTimeOffset.UtcNow - oldestDue).TotalMinutes;
        }

        return (
            await ScalarCountAsync(connection, "SELECT COUNT(*) FROM sessions", cancellationToken),
            await ScalarCountAsync(connection, "SELECT COUNT(*) FROM memories", cancellationToken),
            await ScalarCountAsync(connection, "SELECT COUNT(*) FROM approvals WHERE status = 'pending'", cancellationToken),
            await ScalarCountAsync(connection, "SELECT COUNT(*) FROM scheduled_tasks WHERE enabled = 1", cancellationToken),
            overdueCount,
            oldestDueAgeMinutes,
            latestRunUtc
        );
    }

    private static async Task<long> ScalarCountAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is long l ? l : Convert.ToInt64(value ?? 0);
    }

    private static async Task<string?> ScalarStringAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            return null;
        }

        return value.ToString();
    }

    private static string ReadLatestBackupAge()
    {
        var candidates = new[] { "/var/backups/elrucio", Path.Combine("data", "backups") };
        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var latest = Directory.GetFiles(dir, "elrucio-backup-*.tar.gz")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is null)
            {
                continue;
            }

            var age = DateTimeOffset.UtcNow - latest.LastWriteTimeUtc;
            return $"latest={latest.Name}, age={age.TotalHours:F1}h";
        }

        return "no backup archive found";
    }

    private async Task<bool> TryHandleNaturalScheduleAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var match = NaturalScheduleRegex.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["mins"].Value, out var minutes) || minutes <= 0)
        {
            await SendTextAsync(chatId, "I couldn't parse the schedule delay. Try: schedule in 5 minutes to <task>", cancellationToken);
            return true;
        }

        var prompt = match.Groups["prompt"].Value.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await SendTextAsync(chatId, "I couldn't parse the task prompt. Try: schedule in 5 minutes to tell me a joke", cancellationToken);
            return true;
        }

        var runAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var binding = await sessionStore.GetAsync(chatId, cancellationToken)
                      ?? new SessionBinding(chatId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await sessionStore.UpsertAsync(binding with { LastActiveUtc = DateTimeOffset.UtcNow }, cancellationToken);

        var item = new ScheduledTaskItem(
            Guid.NewGuid().ToString("N")[..8],
            chatId,
            binding.SessionId,
            $"once:{runAt:O}",
            prompt,
            true,
            runAt,
            null,
            DateTimeOffset.UtcNow,
            "telegram",
            chatId);

        await scheduledTaskStore.InsertAsync(item, cancellationToken);
        await SendTextAsync(chatId, $"Scheduled one-time task {item.Id} for {runAt:O} (in {minutes} minute(s)).", cancellationToken);
        return true;
    }
}

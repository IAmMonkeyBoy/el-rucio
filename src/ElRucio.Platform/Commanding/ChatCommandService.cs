using Cronos;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ElRucio.Memory.Sqlite;
using ElRucio.Platform.Orchestration;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Commanding;

public sealed class ChatCommandService(
    ChatOrchestrator orchestrator,
    SqliteDb sqliteDb,
    ISessionStore sessionStore,
    IScheduledTaskStore scheduledTaskStore,
    IOptions<ElRucioOptions> appOptions,
    IOptions<VoiceOptions> voiceOptions,
    IOptions<VideoOptions> videoOptions)
{
    private static readonly Regex NaturalScheduleRegex = new(
        @"schedule(?:\s+a\s+task)?\s+(?:for|in)\s+(?<mins>\d+)\s+min(?:ute)?s?(?:\s+from\s+now)?\s+to\s+(?<prompt>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ElRucioOptions _app = appOptions.Value;
    private readonly VoiceOptions _voice = voiceOptions.Value;
    private readonly VideoOptions _video = videoOptions.Value;
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    public async Task<bool> TryHandleAsync(
        ConversationRef conversation,
        string text,
        Func<ConversationRef, string, CancellationToken, Task> sendText,
        CancellationToken cancellationToken)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith('/'))
        {
            await HandleCommandAsync(conversation, trimmed, sendText, cancellationToken);
            return true;
        }

        return await TryHandleNaturalScheduleAsync(conversation, trimmed, sendText, cancellationToken);
    }

    private async Task HandleCommandAsync(
        ConversationRef conversation,
        string command,
        Func<ConversationRef, string, CancellationToken, Task> sendText,
        CancellationToken cancellationToken)
    {
        var parts = command.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var rawHead = parts[0].ToLowerInvariant();
        var head = rawHead.Split('@', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        switch (head)
        {
            case "/start":
                await sendText(conversation, "El Rucio connected.", cancellationToken);
                break;
            case "/status":
            case "/stat":
                await sendText(conversation, await BuildStatusMessageAsync(cancellationToken), cancellationToken);
                break;
            case "/diag":
                await sendText(conversation, await BuildDiagMessageAsync(cancellationToken), cancellationToken);
                break;
            case "/newchat":
                await sessionStore.DeleteAsync(conversation, cancellationToken);
                await sendText(conversation, "Started a fresh chat session.", cancellationToken);
                break;
            case "/approve" when parts.Length >= 2:
                await sendText(conversation, await orchestrator.ApproveAsync(conversation.ChatKey, parts[1], cancellationToken), cancellationToken);
                break;
            case "/cancel" when parts.Length >= 2:
                await sendText(conversation, await orchestrator.CancelAsync(conversation.ChatKey, parts[1], cancellationToken), cancellationToken);
                break;
            case "/voice" when parts.Length >= 2:
                _voice.ForceVoiceReply = string.Equals(parts[1], "on", StringComparison.OrdinalIgnoreCase);
                await sendText(conversation, $"Voice reply toggle now: {(_voice.ForceVoiceReply ? "on" : "off")}", cancellationToken);
                break;
            case "/schedule":
                await HandleScheduleCommandAsync(conversation, parts, sendText, cancellationToken);
                break;
            default:
                await sendText(conversation, "Unknown command.", cancellationToken);
                break;
        }
    }

    private async Task HandleScheduleCommandAsync(
        ConversationRef conversation,
        string[] parts,
        Func<ConversationRef, string, CancellationToken, Task> sendText,
        CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            await sendText(conversation, "Usage: /schedule create|list|pause|resume|delete", cancellationToken);
            return;
        }

        var action = parts[1].ToLowerInvariant();
        if (action == "list")
        {
            var items = await scheduledTaskStore.ListAsync(conversation, cancellationToken);
            var lines = items.Count == 0
                ? "No schedules."
                : string.Join("\n", items.Select(x => $"{x.Id} | {(x.Enabled ? "on" : "off")} | {x.Cron} | next {x.NextRunUtc:O}"));
            await sendText(conversation, lines, cancellationToken);
            return;
        }

        if (action == "create" && parts.Length >= 3)
        {
            var payload = parts[2];
            var marker = payload.IndexOf('|');
            if (marker <= 0)
            {
                await sendText(conversation, "Use: /schedule create <cron>|<prompt>", cancellationToken);
                return;
            }

            var cronText = payload[..marker].Trim();
            var prompt = payload[(marker + 1)..].Trim();
            var cron = CronExpression.Parse(cronText);
            var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc) ?? DateTimeOffset.UtcNow.AddMinutes(5);

            var binding = await sessionStore.GetAsync(conversation, cancellationToken)
                          ?? new SessionBinding(conversation.ChatKey, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await sessionStore.UpsertAsync(binding with { LastActiveUtc = DateTimeOffset.UtcNow }, cancellationToken);

            var item = new ScheduledTaskItem(
                Guid.NewGuid().ToString("N")[..8],
                conversation.ChatKey,
                binding.SessionId,
                cronText,
                prompt,
                true,
                next,
                null,
                DateTimeOffset.UtcNow,
                conversation.Provider,
                conversation.ConversationId,
                conversation.ThreadId,
                conversation.UserId);

            await scheduledTaskStore.InsertAsync(item, cancellationToken);
            await sendText(conversation, $"Schedule created: {item.Id}", cancellationToken);
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

            await sendText(conversation, $"Schedule {action} complete for {id}", cancellationToken);
            return;
        }

        await sendText(conversation, "Invalid /schedule command.", cancellationToken);
    }

    private async Task<bool> TryHandleNaturalScheduleAsync(
        ConversationRef conversation,
        string text,
        Func<ConversationRef, string, CancellationToken, Task> sendText,
        CancellationToken cancellationToken)
    {
        var match = NaturalScheduleRegex.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["mins"].Value, out var minutes) || minutes <= 0)
        {
            await sendText(conversation, "I couldn't parse the schedule delay. Try: schedule in 5 minutes to <task>", cancellationToken);
            return true;
        }

        var prompt = match.Groups["prompt"].Value.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await sendText(conversation, "I couldn't parse the task prompt. Try: schedule in 5 minutes to tell me a joke", cancellationToken);
            return true;
        }

        var runAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var binding = await sessionStore.GetAsync(conversation, cancellationToken)
                      ?? new SessionBinding(conversation.ChatKey, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await sessionStore.UpsertAsync(binding with { LastActiveUtc = DateTimeOffset.UtcNow }, cancellationToken);

        var item = new ScheduledTaskItem(
            Guid.NewGuid().ToString("N")[..8],
            conversation.ChatKey,
            binding.SessionId,
            $"once:{runAt:O}",
            prompt,
            true,
            runAt,
            null,
            DateTimeOffset.UtcNow,
            conversation.Provider,
            conversation.ConversationId,
            conversation.ThreadId,
            conversation.UserId);

        await scheduledTaskStore.InsertAsync(item, cancellationToken);
        await sendText(conversation, $"Scheduled one-time task {item.Id} for {runAt:O} (in {minutes} minute(s)).", cancellationToken);
        return true;
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
}

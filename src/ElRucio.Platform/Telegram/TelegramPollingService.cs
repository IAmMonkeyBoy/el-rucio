using Cronos;
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
    ChatOrchestrator orchestrator,
    ISessionStore sessionStore,
    IScheduledTaskStore scheduledTaskStore,
    IVoiceTranscriber voiceTranscriber,
    IOptions<ElRucioOptions> appOptions,
    IOptions<VoiceOptions> voiceOptions,
    ILogger<TelegramPollingService> logger) : BackgroundService, IOutboundMessenger
{
    private readonly ElRucioOptions _app = appOptions.Value;
    private readonly VoiceOptions _voice = voiceOptions.Value;
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

    private bool IsAllowed(string chatId)
        => _app.AllowedChatIds.Count == 0 || _app.AllowedChatIds.Contains(chatId);

    private async Task HandleMessageAsync(string chatId, TelegramMessage message, CancellationToken cancellationToken)
    {
        var text = message.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(text) && text.StartsWith('/'))
        {
            await HandleCommandAsync(chatId, text, cancellationToken);
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

        if (string.IsNullOrWhiteSpace(text))
        {
            await SendTextAsync(chatId, "Send text, or send voice if STT is enabled.", cancellationToken);
            return;
        }

        var response = await orchestrator.HandleUserPromptAsync(chatId, text, cancellationToken);
        await SendTextAsync(chatId, response, cancellationToken);
    }

    private async Task HandleCommandAsync(string chatId, string command, CancellationToken cancellationToken)
    {
        var parts = command.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0].ToLowerInvariant();

        switch (head)
        {
            case "/start":
                await SendTextAsync(chatId, "El Rucio connected.", cancellationToken);
                break;
            case "/status":
                await SendTextAsync(chatId, orchestrator.BuildStatusMessage(), cancellationToken);
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
                DateTimeOffset.UtcNow);

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
}

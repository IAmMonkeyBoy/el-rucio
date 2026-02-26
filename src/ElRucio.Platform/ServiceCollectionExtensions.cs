using ElRucio.Platform.Commanding;
using ElRucio.Platform.Orchestration;
using ElRucio.Platform.Slack;
using ElRucio.Platform.Telegram;
using ElRucio.Platform.Video;
using ElRucio.Platform.Voice;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElRucioPlatform(this IServiceCollection services)
    {
        services.AddHttpClient<TelegramApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(40);
        });
        services.AddHttpClient<OpenAiSpeechToText>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient<OpenAiVisionAnalyzer>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<SlackApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(40);
        });

        services.AddSingleton<ChatOrchestrator>();
        services.AddSingleton<ChatCommandService>();
        services.AddSingleton<IVideoAnalyzer>(sp =>
        {
            var video = sp.GetRequiredService<IOptions<VideoOptions>>().Value;
            if (video.Enabled && string.Equals(video.Provider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<OpenAiVisionAnalyzer>();
            }

            return new VideoAnalyzerStub();
        });
        services.AddSingleton<IVoiceTranscriber>(sp =>
        {
            var voice = sp.GetRequiredService<IOptions<VoiceOptions>>().Value;
            if (voice.Enabled && string.Equals(voice.SttProvider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<OpenAiSpeechToText>();
            }

            return new NullVoiceTranscriber();
        });

        services.AddSingleton<TelegramPollingService>();
        services.AddSingleton<SlackSocketModeService>();

        services.AddSingleton<IOutboundMessenger>(sp =>
        {
            var platform = sp.GetRequiredService<IOptions<PlatformOptions>>().Value;
            if (string.Equals(platform.Provider, "slack", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<SlackSocketModeService>();
            }

            return sp.GetRequiredService<TelegramPollingService>();
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            var platform = sp.GetRequiredService<IOptions<PlatformOptions>>().Value;
            if (string.Equals(platform.Provider, "slack", StringComparison.OrdinalIgnoreCase))
            {
                var slack = sp.GetRequiredService<IOptions<SlackOptions>>().Value;
                if (string.IsNullOrWhiteSpace(slack.AppToken) || string.IsNullOrWhiteSpace(slack.BotToken))
                {
                    throw new InvalidOperationException("Slack provider selected but Slack:AppToken or Slack:BotToken is missing.");
                }

                return sp.GetRequiredService<SlackSocketModeService>();
            }

            var telegram = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            if (string.IsNullOrWhiteSpace(telegram.BotToken))
            {
                throw new InvalidOperationException("Telegram provider selected but Telegram:BotToken is missing.");
            }

            return sp.GetRequiredService<TelegramPollingService>();
        });
        return services;
    }
}

internal sealed class NullVoiceTranscriber : IVoiceTranscriber
{
    public Task<string?> TranscribeAsync(string filePath, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}

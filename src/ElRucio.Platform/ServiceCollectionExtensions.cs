using ElRucio.Platform.Orchestration;
using ElRucio.Platform.Telegram;
using ElRucio.Platform.Video;
using ElRucio.Platform.Voice;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Options;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton<ChatOrchestrator>();
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
        services.AddSingleton<IOutboundMessenger>(sp => sp.GetRequiredService<TelegramPollingService>());
        services.AddHostedService(sp => sp.GetRequiredService<TelegramPollingService>());
        return services;
    }
}

internal sealed class NullVoiceTranscriber : IVoiceTranscriber
{
    public Task<string?> TranscribeAsync(string filePath, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}

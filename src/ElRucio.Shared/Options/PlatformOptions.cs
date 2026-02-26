using System.ComponentModel.DataAnnotations;

namespace ElRucio.Shared.Options;

public sealed class PlatformOptions
{
    [Required]
    public string Provider { get; set; } = "telegram";
}

public sealed class TelegramOptions
{
    [Required]
    public string BotToken { get; set; } = string.Empty;

    [Required]
    public string Mode { get; set; } = "Polling";

    public string? WebhookPublicUrl { get; set; }
}

public sealed class SlackOptions
{
    public string Mode { get; set; } = "SocketMode";
    public string? AppToken { get; set; }
    public string? BotToken { get; set; }
    public string? SigningSecret { get; set; }
    public string? BotUserId { get; set; }
    public int DedupWindowSeconds { get; set; } = 300;
}

public sealed class CopilotOptions
{
    public string? Model { get; set; } = "gpt-5";
    public string? CliPath { get; set; }
    public string? CliUrl { get; set; }
}

public sealed class MemoryOptions
{
    [Required]
    public string Mode { get; set; } = "Full";

    public int SimpleTurnCount { get; set; } = 12;
    public int FtsTopK { get; set; } = 3;
    public int RecencyCount { get; set; } = 5;
    public double SalienceStart { get; set; } = 1.0;
    public double SalienceAccessIncrement { get; set; } = 0.1;
    public double SalienceMax { get; set; } = 5.0;
    public double SalienceDailyDecayRate { get; set; } = 0.98;
    public double SalienceDeleteThreshold { get; set; } = 0.1;
}

public sealed class SchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
}

public sealed class VoiceOptions
{
    public bool Enabled { get; set; }
    public string? SttProvider { get; set; } = "openai";
    public string? OpenAiApiKey { get; set; }
    public bool ForceVoiceReply { get; set; }
}

public sealed class VideoOptions
{
    public bool Enabled { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string AnalysisModel { get; set; } = "gpt-4o-mini";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int FrameSampleCount { get; set; } = 4;
}

public sealed class ServiceInstallOptions
{
    public bool Enabled { get; set; }
    public bool GenerateOnly { get; set; } = true;
}

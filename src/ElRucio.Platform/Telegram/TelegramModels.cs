using System.Text.Json.Serialization;

namespace ElRucio.Platform.Telegram;

public sealed class TelegramApiEnvelope<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }
}

public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("chat")]
    public TelegramChat? Chat { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("voice")]
    public TelegramVoice? Voice { get; set; }

    [JsonPropertyName("video")]
    public TelegramMedia? Video { get; set; }

    [JsonPropertyName("photo")]
    public List<TelegramMedia>? Photo { get; set; }
}

public sealed class TelegramChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

public sealed class TelegramVoice
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;
}

public sealed class TelegramMedia
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;
}

public sealed class TelegramFileMeta
{
    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;
}

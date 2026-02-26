using System.Net.Http.Json;
using System.Text.Json;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Telegram;

public sealed class TelegramApiClient(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramApiClient> logger)
{
    private readonly TelegramOptions _options = options.Value;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private string BotBase => $"https://api.telegram.org/bot{_options.BotToken}";
    private string FileBase => $"https://api.telegram.org/file/bot{_options.BotToken}";

    public async Task<List<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var url = $"{BotBase}/getUpdates?timeout=30&offset={offset}";
        var payload = await httpClient.GetFromJsonAsync<TelegramApiEnvelope<List<TelegramUpdate>>>(url, _jsonOptions, cancellationToken);
        return payload?.Result ?? [];
    }

    public async Task SendTextAsync(string chatId, string htmlText, CancellationToken cancellationToken)
    {
        var endpoint = $"{BotBase}/sendMessage";
        var body = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = htmlText,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = true
        };

        using var response = await httpClient.PostAsJsonAsync(endpoint, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Telegram sendMessage failed ({Status}): {Body}", response.StatusCode, detail);
        }
    }

    public async Task<string?> DownloadFileAsync(string fileId, string destinationRoot, CancellationToken cancellationToken)
    {
        var fileMeta = await httpClient.GetFromJsonAsync<TelegramApiEnvelope<TelegramFileMeta>>(
            $"{BotBase}/getFile?file_id={Uri.EscapeDataString(fileId)}",
            _jsonOptions,
            cancellationToken);

        var filePath = fileMeta?.Result?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var targetDir = Path.Combine(destinationRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(targetDir);

        var filename = Path.GetFileName(filePath);
        var destinationPath = Path.Combine(targetDir, filename);
        await using var stream = await httpClient.GetStreamAsync($"{FileBase}/{filePath}", cancellationToken);
        await using var fs = File.Create(destinationPath);
        await stream.CopyToAsync(fs, cancellationToken);
        return destinationPath;
    }
}

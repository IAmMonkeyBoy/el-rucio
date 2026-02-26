using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Voice;

public sealed class OpenAiSpeechToText(HttpClient httpClient, IOptions<VoiceOptions> options) : IVoiceTranscriber
{
    private readonly VoiceOptions _options = options.Value;

    public async Task<string?> TranscribeAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
        {
            return null;
        }

        var uploadPath = MaybeRenameOgaToOgg(filePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("whisper-1"), "model");

        var bytes = await File.ReadAllBytesAsync(uploadPath, cancellationToken);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        content.Add(fileContent, "file", Path.GetFileName(uploadPath));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (payload.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        return null;
    }

    private static string MaybeRenameOgaToOgg(string filePath)
    {
        if (!filePath.EndsWith(".oga", StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }

        var renamed = Path.ChangeExtension(filePath, ".ogg");
        File.Copy(filePath, renamed, true);
        return renamed;
    }
}

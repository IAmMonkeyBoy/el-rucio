using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Slack;

public sealed class SlackApiClient(HttpClient httpClient, IOptions<SlackOptions> options, ILogger<SlackApiClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SlackOptions _options = options.Value;

    public async Task<Uri> OpenSocketConnectionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/apps.connections.open");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AppToken);
        request.Content = JsonContent.Create(new { });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Slack apps.connections.open failed ({StatusCode})", response.StatusCode);
            throw new InvalidOperationException("Slack socket connection request failed.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "unknown_error";
            throw new InvalidOperationException($"Slack apps.connections.open returned error: {error}");
        }

        var url = root.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Slack apps.connections.open returned invalid url.");
        }

        return uri;
    }

    public async Task SendMessageAsync(string channelId, string text, string? threadTs, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["channel"] = channelId,
            ["text"] = text
        };

        if (!string.IsNullOrWhiteSpace(threadTs))
        {
            payload["thread_ts"] = threadTs;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BotToken);
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Slack chat.postMessage HTTP failure ({StatusCode}) for channel {ChannelId}", response.StatusCode, channelId);
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "unknown_error";
            logger.LogWarning("Slack chat.postMessage API error for channel {ChannelId}: {Error}", channelId, error);
        }
    }

    public async Task<string?> DownloadFileAsync(string fileUrl, string destinationRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        using var response = await SendWithBotAuthFollowingRedirectsAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Slack file download failed ({StatusCode})", response.StatusCode);
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Slack file download returned HTML for {Url}; check token scopes or redirect auth.", uri.Host);
            return null;
        }

        var targetDir = Path.Combine(destinationRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(targetDir);

        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"slack-file-{Guid.NewGuid():N}";
        }

        var destinationPath = Path.Combine(targetDir, fileName);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output, cancellationToken);
        return destinationPath;
    }

    private async Task<HttpResponseMessage> SendWithBotAuthFollowingRedirectsAsync(Uri uri, CancellationToken cancellationToken)
    {
        var current = uri;

        for (var redirectCount = 0; redirectCount < 6; redirectCount++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BotToken);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                var next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);

                response.Dispose();
                request.Dispose();
                current = next;
                continue;
            }

            request.Dispose();
            return response;
        }

        throw new InvalidOperationException("Slack file download exceeded redirect limit.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.MovedPermanently
           || statusCode == HttpStatusCode.Redirect
           || statusCode == HttpStatusCode.RedirectMethod
           || statusCode == HttpStatusCode.TemporaryRedirect
           || (int)statusCode == 308;
}

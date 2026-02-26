using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ElRucio.Platform.Slack;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Tests;

public class SlackApiClientTests
{
    [Fact]
    public async Task OpenSocketConnectionAsync_ReturnsSocketUrl()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{" + "\"ok\":true,\"url\":\"wss://wss-primary.slack.com/link\"}" , Encoding.UTF8, "application/json")
        });

        var client = CreateClient(handler);

        var socketUri = await client.OpenSocketConnectionAsync(CancellationToken.None);

        Assert.Equal("wss://wss-primary.slack.com/link", socketUri.ToString());
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal("https://slack.com/api/apps.connections.open", request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("xapp-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendMessageAsync_PostsToChatEndpoint_WithThreadAndAuth()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{" + "\"ok\":true}" , Encoding.UTF8, "application/json")
        });

        var client = CreateClient(handler);

        await client.SendMessageAsync("C123", "hello", "1700000.0001", CancellationToken.None);

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal("https://slack.com/api/chat.postMessage", request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("xoxb-token", request.Headers.Authorization?.Parameter);

        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("\"channel\":\"C123\"", body);
        Assert.Contains("\"text\":\"hello\"", body);
        Assert.Contains("\"thread_ts\":\"1700000.0001\"", body);
    }

    [Fact]
    public async Task DownloadFileAsync_DownloadsWithBotAuth()
    {
        var fileBytes = Encoding.UTF8.GetBytes("sample-data");
        var handler = new SequenceHandler(request =>
        {
            if (request.RequestUri?.ToString() == "https://files.slack.com/files-pri/T1-F1/test.wav")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found")
            };
        });

        var client = CreateClient(handler);
        var root = Path.Combine(Path.GetTempPath(), "elrucio-slack-api-tests", Guid.NewGuid().ToString("N"));

        var downloaded = await client.DownloadFileAsync("https://files.slack.com/files-pri/T1-F1/test.wav", root, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(downloaded));
        Assert.True(File.Exists(downloaded));
        var text = await File.ReadAllTextAsync(downloaded!, CancellationToken.None);
        Assert.Equal("sample-data", text);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("xoxb-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task DownloadFileAsync_FollowsRedirectAndKeepsAuth()
    {
        var fileBytes = Encoding.UTF8.GetBytes("redirect-bytes");
        var handler = new SequenceHandler(request =>
        {
            var url = request.RequestUri?.ToString();
            if (url == "https://files.slack.com/files-pri/T1-F2/image.jpg")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://downloads.slack-edge.com/files-pri/T1-F2/image.jpg");
                return response;
            }

            if (url == "https://downloads.slack-edge.com/files-pri/T1-F2/image.jpg")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileBytes)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") }
                    }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = CreateClient(handler);
        var root = Path.Combine(Path.GetTempPath(), "elrucio-slack-api-tests", Guid.NewGuid().ToString("N"));

        var downloaded = await client.DownloadFileAsync("https://files.slack.com/files-pri/T1-F2/image.jpg", root, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(downloaded));
        var written = await File.ReadAllTextAsync(downloaded!, CancellationToken.None);
        Assert.Equal("redirect-bytes", written);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, req =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("xoxb-token", req.Headers.Authorization?.Parameter);
        });
    }

    private static SlackApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new SlackOptions
        {
            AppToken = "xapp-token",
            BotToken = "xoxb-token"
        });

        return new SlackApiClient(httpClient, options, NullLogger<SlackApiClient>.Instance);
    }

    private sealed class SequenceHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = CloneRequest(request);
            Requests.Add(clone);
            return Task.FromResult(_responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);
            foreach (var header in original.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (original.Content is not null)
            {
                var content = original.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(content, Encoding.UTF8, original.Content.Headers.ContentType?.MediaType ?? "application/json");
                foreach (var header in original.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
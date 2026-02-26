using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Video;

public sealed class OpenAiVisionAnalyzer(HttpClient httpClient, IOptions<VideoOptions> options) : IVideoAnalyzer
{
    private readonly VideoOptions _options = options.Value;

    public async Task<string> AnalyzeAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return "Video/image analysis key not configured.";
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var imageMime = GetImageMimeType(ext);

        if (imageMime is not null)
        {
            return await AnalyzeImageAsync(filePath, imageMime, cancellationToken);
        }

        if (IsVideo(ext))
        {
            return await AnalyzeVideoAsync(filePath, cancellationToken);
        }

        return $"Unsupported media type for analysis: {ext}";
    }

    private async Task<string> AnalyzeVideoAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!await CanRunFfmpegAsync(cancellationToken))
        {
            return "Video analysis requires ffmpeg on the host. Install ffmpeg and retry.";
        }

        var frameDir = Path.Combine(Path.GetTempPath(), "elrucio-video", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(frameDir);

        try
        {
            var framePattern = Path.Combine(frameDir, "frame-%03d.jpg");
            var frameCount = Math.Max(1, _options.FrameSampleCount);

            var args = $"-hide_banner -loglevel error -i \"{filePath}\" -vf \"fps=1\" -frames:v {frameCount} \"{framePattern}\"";
            var exitCode = await RunProcessAsync(_options.FfmpegPath, args, cancellationToken);
            if (exitCode != 0)
            {
                return "Video analysis failed while extracting frames.";
            }

            var frames = Directory.GetFiles(frameDir, "frame-*.jpg").OrderBy(x => x).ToList();
            if (frames.Count == 0)
            {
                return "Video analysis could not extract frames from this video.";
            }

            var analyses = new List<string>();
            foreach (var frame in frames)
            {
                var analysis = await AnalyzeImageAsync(frame, "image/jpeg", cancellationToken);
                analyses.Add(analysis);
            }

            var nonGenericAnalyses = analyses
                .Where(analysis => !LooksLikeNonVisualGenericResponse(analysis))
                .ToList();

            if (nonGenericAnalyses.Count == 0)
            {
                return "Video frames were extracted, but visual analysis returned generic/non-visual output for all frames. Please resend the video (or a shorter clip) and retry.";
            }

            var summary = await SummarizeFrameAnalysesAsync(analyses, cancellationToken);
            if (LooksLikeNonVisualGenericResponse(summary))
            {
                return BuildDeterministicFrameSummary(nonGenericAnalyses);
            }

            return summary;
        }
        finally
        {
            try
            {
                Directory.Delete(frameDir, true);
            }
            catch
            {
            }
        }
    }

    private async Task<string> AnalyzeImageAsync(string filePath, string imageMime, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var base64 = Convert.ToBase64String(bytes);
        var dataUrl = $"data:{imageMime};base64,{base64}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.AnalysisModel,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are a vision assistant. The image is provided in this request. Never claim you cannot view or access the image. Describe only what is visually present and avoid generic disclaimers."
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Analyze this image and summarize key details and actionable insights in 5 bullets max." },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            max_tokens = 500
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return $"Image analysis failed ({(int)response.StatusCode}): {body}";
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return ExtractMessageContent(payload);
    }

    private async Task<string> SummarizeFrameAnalysesAsync(List<string> frameAnalyses, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < frameAnalyses.Count; i++)
        {
            sb.AppendLine($"Frame {i + 1}: {frameAnalyses[i]}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.AnalysisModel,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are summarizing already-computed visual frame analyses. Never claim you cannot see images or request additional context. Use only the supplied frame analyses."
                },
                new
                {
                    role = "user",
                    content = "You are summarizing analyses from sampled video frames. Return: (1) concise timeline, (2) key entities/actions, (3) risks/anomalies, (4) suggested next actions.\n\n" + sb
                }
            },
            max_tokens = 700
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return $"Video summary failed ({(int)response.StatusCode}): {body}";
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return ExtractMessageContent(payload);
    }

    private static string ExtractMessageContent(JsonElement payload)
    {
        if (payload.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? "No analysis content returned.";
            }
        }

        return "No analysis content returned.";
    }

    private static string? GetImageMimeType(string ext)
        => ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null
        };

    private static bool IsVideo(string ext)
        => ext is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi";

    private static bool LooksLikeNonVisualGenericResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var lower = text.ToLowerInvariant();
        return lower.Contains("no images")
               || lower.Contains("no specific entities")
               || lower.Contains("without direct visual")
               || lower.Contains("cannot identify")
               || lower.Contains("can’t see")
               || lower.Contains("can't see")
               || lower.Contains("cannot see")
               || lower.Contains("provide context")
               || lower.Contains("user-supplied context")
               || lower.Contains("no visual") && lower.Contains("provided");
    }

    private static string BuildDeterministicFrameSummary(List<string> frameAnalyses)
    {
        var lines = new List<string>
        {
            "Video analysis summary (from sampled frames):"
        };

        for (var index = 0; index < frameAnalyses.Count; index++)
        {
            lines.Add($"- Frame {index + 1}: {frameAnalyses[index]}");
        }

        return string.Join("\n", lines);
    }

    private async Task<bool> CanRunFfmpegAsync(CancellationToken cancellationToken)
    {
        var code = await RunProcessAsync(_options.FfmpegPath, "-version", cancellationToken, swallowErrors: true);
        return code == 0;
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, bool swallowErrors = false)
    {
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
            return process.ExitCode;
        }
        catch when (swallowErrors)
        {
            return -1;
        }
    }
}

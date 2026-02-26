using ElRucio.Shared.Contracts;

namespace ElRucio.Platform.Video;

public sealed class VideoAnalyzerStub : IVideoAnalyzer
{
    public Task<string> AnalyzeAsync(string filePath, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Video analysis provider wiring is not implemented. TODO: wire provider API according to chosen provider docs.");
    }
}

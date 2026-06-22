namespace StemForge.Tests.Fakes;

/// <summary>
/// Returns a canned thumbnail path (or null) without performing any HTTP work. Records the
/// directory it was asked to write into so tests can assert the fetch target.
/// </summary>
internal sealed class StubThumbnailFetcher : IThumbnailFetcher
{
    private readonly string? _result;

    public StubThumbnailFetcher(string? result = null) => _result = result;

    public string? LastOutDir { get; private set; }

    public int CallCount { get; private set; }

    public Task<string?> DownloadAsync(string? url, string outDir, CancellationToken ct = default)
    {
        CallCount++;
        LastOutDir = outDir;
        return Task.FromResult(_result);
    }
}

namespace StemForge.Core.Downloading;

public interface IThumbnailFetcher
{
    /// <summary>
    /// Downloads the thumbnail image at <paramref name="url"/> into <paramref name="outDir"/>
    /// and returns the local path. Returns null on failure (non-fatal).
    /// </summary>
    Task<string?> DownloadAsync(string? url, string outDir, CancellationToken ct = default);
}

public sealed class ThumbnailFetcher(IHttpClientFactory factory) : IThumbnailFetcher
{
    private readonly IHttpClientFactory _factory = factory;

    public async Task<string?> DownloadAsync(
        string? url,
        string outDir,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            // Derive extension from URL path; default to .jpg which ffmpeg handles universally.
            var uriPath = new Uri(url).LocalPath;
            var ext = Path.GetExtension(uriPath);
            if (string.IsNullOrEmpty(ext))
                ext = ".jpg";

            var dest = Path.Combine(outDir, $"thumbnail{ext}");
            var http = _factory.CreateClient("thumbnail");
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(dest, bytes, ct);
            return dest;
        }
        catch (Exception ex)
        {
            AppLogger.Debug("yt-dlp", $"Thumbnail download failed: {ex.Message}");
            return null;
        }
    }
}

namespace StemForge.Models;

/// <summary>
/// Metadata harvested from a source — either a local audio file (via TagLibSharp) or a
/// URL-resolved track (via yt-dlp). Passed through the separation pipeline so it can be
/// written to every output stem after separation is complete.
/// </summary>
public sealed record SourceTagInfo
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public uint Year { get; init; }

    /// <summary>Raw bytes of the front-cover image (JPEG or PNG preferred).</summary>
    public byte[]? CoverArtBytes { get; init; }

    /// <summary>MIME type of <see cref="CoverArtBytes"/>. Defaults to image/jpeg.</summary>
    public string CoverArtMimeType { get; init; } = "image/jpeg";
}

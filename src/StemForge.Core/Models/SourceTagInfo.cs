namespace StemForge.Core.Models;

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

    // ── Exact-source provenance (URL jobs only; null for local-file jobs) ──────

    /// <summary>Literal source URL the audio was fetched from, e.g. the YouTube watch page.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Source audio codec reported by yt-dlp, e.g. "opus" or "mp4a.40.2".</summary>
    public string? SourceCodec { get; init; }

    /// <summary>Source audio bitrate in kbps reported by yt-dlp.</summary>
    public double? SourceBitrateKbps { get; init; }

    /// <summary>yt-dlp format-id of the exact format that was downloaded, e.g. "251".</summary>
    public string? SourceFormatId { get; init; }
}

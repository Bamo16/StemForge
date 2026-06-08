namespace StemForge.Core.Models;

public sealed record YtDlpMetadata(
    string SourceUrl,
    string Title,
    string? Artist,
    string? Uploader,
    string? SourceCodec,
    double? SourceBitrateKbps,
    double? DurationSeconds,
    string? FormatId,
    string MediaUrl,
    string? ThumbnailUrl = null,
    IReadOnlyList<YtDlpFormat>? AudioFormats = null,
    string? Extractor = null
)
{
    /// <summary>"Artist - Title" when artist is available, plain Title otherwise.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

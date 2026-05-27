namespace StemForge.Models;

/// <summary>Top-level object returned by yt-dlp --dump-single-json.</summary>
public sealed record YtDlpVideoInfo
{
    public string Title { get; init; } = "";
    public string? Artist { get; init; }
    public string? Uploader { get; init; }
    public string? Acodec { get; init; }
    public double? Abr { get; init; }
    public double? Duration { get; init; }
    public string? FormatId { get; init; }
    public string? Url { get; init; }
    public string? FormatNote { get; init; }
    public string? Ext { get; init; }
    public double? Tbr { get; init; }
    public double? Asr { get; init; }

    /// <summary>URL of the highest-quality thumbnail. Null for local files.</summary>
    public string? Thumbnail { get; init; }

    /// <summary>Lowercase extractor name from yt-dlp, e.g. "youtube".</summary>
    public string? Extractor { get; init; }

    public List<YtDlpFormat>? Formats { get; init; }
}

public sealed record YtDlpFormat
{
    public string? FormatId { get; init; }
    public string? Ext { get; init; }
    public string? Acodec { get; init; }
    public string? Vcodec { get; init; }
    public double? Abr { get; init; }
    public string? FormatNote { get; init; }
    public string? Url { get; init; }
    public double? Tbr { get; init; }
    public double? Asr { get; init; }
    public long? Filesize { get; init; }
}

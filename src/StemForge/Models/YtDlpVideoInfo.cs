namespace StemForge.Models;

/// <summary>Top-level object returned by yt-dlp --dump-single-json.</summary>
public sealed record YtDlpVideoInfo
{
    public string Title { get; init; } = "";
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
}

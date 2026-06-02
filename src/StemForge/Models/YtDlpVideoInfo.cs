using System.Text.Json.Serialization;

namespace StemForge.Models;

/// <summary>Top-level object returned by yt-dlp --dump-single-json.</summary>
public sealed record YtDlpVideoInfo
{
    public string Title { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public string? Uploader { get; init; }
    public double? Duration { get; init; }
    public string? FormatId { get; init; }
    public string? FormatNote { get; init; }
    public string? Url { get; init; }

    /// <summary>Canonical page URL for the source, e.g. the YouTube watch page.</summary>
    [JsonPropertyName("webpage_url")]
    public string? WebpageUrl { get; init; }

    /// <summary>The URL originally requested, before any redirects/normalisation by yt-dlp.</summary>
    [JsonPropertyName("original_url")]
    public string? OriginalUrl { get; init; }

    [JsonPropertyName("ext")]
    public string? Extension { get; init; }

    [JsonPropertyName("acodec")]
    public string? AudioCodec { get; init; }

    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; init; }

    [JsonPropertyName("abr")]
    public double? AverageAudioBitrate { get; init; }

    [JsonPropertyName("tbr")]
    public double? AverageTotalBitrate { get; init; }

    [JsonPropertyName("asr")]
    public int? AudioSampleRate { get; init; }
    public int? AudioChannels { get; init; }

    [JsonPropertyName("filesize")]
    public long? FileSize { get; init; }

    [JsonPropertyName("filesize_approx")]
    public long? FileSizeApprox { get; init; }

    /// <summary>URL of the highest-quality thumbnail. Null for local files.</summary>
    public string? Thumbnail { get; init; }

    /// <summary>Lowercase extractor name from yt-dlp, e.g. "youtube".</summary>
    public string? Extractor { get; init; }

    public List<YtDlpFormat> Formats { get; init; } = [];

    [JsonIgnore]
    public List<YtDlpFormat> AudioOnlyFormats => [.. Formats.Where(f => f.IsAudioOnly)];

    /// <summary>
    /// Selects the best audio-only format from the formats list.
    /// Prefers 44.1 kHz unless the best non-44.1 kHz option has more than 10% higher bitrate
    /// (audio-separator always resamples to 44.1 kHz, so using a non-44.1 kHz format just adds
    /// a lossy resampling step with no quality benefit).
    /// </summary>
    public YtDlpFormat SelectBestAudioFormat() =>
        AudioOnlyFormats.MaxBy(f =>
            f is { AudioSampleRate: 44100, AudioBitrate: var br441 } ? br441 : f.AudioBitrate * 0.90
        )
        ?? new YtDlpFormat
        {
            FormatId = FormatId,
            FormatNote = FormatNote,
            Url = Url,
            Extension = Extension,
            AudioCodec = AudioCodec,
            VideoCodec = VideoCodec,
            AverageAudioBitrate = AverageAudioBitrate,
            AverageTotalBitrate = AverageTotalBitrate,
            AudioSampleRate = AudioSampleRate,
            AudioChannels = AudioChannels,
            FileSize = FileSize,
            FileSizeApprox = FileSizeApprox,
        };
}

public sealed record YtDlpFormat
{
    public string? FormatId { get; init; }
    public string? FormatNote { get; init; }
    public string? Url { get; init; }

    [JsonPropertyName("ext")]
    public string? Extension { get; init; }

    [JsonPropertyName("acodec")]
    public string? AudioCodec { get; init; }

    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; init; }

    /// <summary>Average audio bitrate in kbps. Set by YouTube; equals tbr for audio-only formats.</summary>
    [JsonPropertyName("abr")]
    public double? AverageAudioBitrate { get; init; }

    /// <summary>
    /// Average total (audio + video) bitrate in kbps. For audio-only formats this equals abr.
    /// yt-dlp's br sort composite checks tbr first, so this is the primary bitrate signal.
    /// </summary>
    [JsonPropertyName("tbr")]
    public double? AverageTotalBitrate { get; init; }

    /// <summary>Audio sampling rate in Hz.</summary>
    [JsonPropertyName("asr")]
    public int? AudioSampleRate { get; init; }

    /// <summary>Number of audio channels.</summary>
    public int? AudioChannels { get; init; }

    /// <summary>The number of bytes, if known in advance.</summary>
    [JsonPropertyName("filesize")]
    public long? FileSize { get; init; }

    /// <summary>An estimate for the number of bytes.</summary>
    [JsonPropertyName("filesize_approx")]
    public long? FileSizeApprox { get; init; }

    [JsonIgnore]
    public bool HasAudio => AudioCodec is not (null or "none");

    [JsonIgnore]
    public bool HasVideo => VideoCodec is not (null or "none");

    /// <summary>
    /// True when the format carries audio, no video track, and has a direct URL.
    /// Formats without a URL (e.g. fragmented DASH) are excluded as they cannot be
    /// streamed directly by ffmpeg without manifest parsing.
    /// </summary>
    [JsonIgnore]
    public bool IsAudioOnly => HasAudio && !HasVideo && Url is not null;

    /// <summary>
    /// Best available bitrate estimate in kbps. abr and tbr are equal for YouTube audio-only
    /// formats; tbr is used as the primary signal when abr is absent (mirrors yt-dlp's own
    /// br sort composite: tbr → vbr → abr).
    /// </summary>
    [JsonIgnore]
    public double AudioBitrate => AverageAudioBitrate ?? AverageTotalBitrate ?? 0;
}

/// <summary>
/// Source-generated serializer context for yt-dlp metadata. The snake_case naming policy lives
/// here, co-located with the DTO it describes, rather than on a distant call site.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(YtDlpVideoInfo))]
internal sealed partial class YtDlpJsonContext : JsonSerializerContext { }

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
    /// Audio-only formats ordered best-first to mirror yt-dlp's own format preference, so the
    /// picker dropdown reads top-down from the highest-quality option. yt-dlp emits formats in
    /// ascending preference (best last); this returns them best-first by applying the same audio
    /// ranking yt-dlp uses by default (bitrate, then channels, then codec, then filesize) with a
    /// deterministic format-id tiebreak. The format yt-dlp would pick by default (see
    /// <see cref="SelectBestAudioFormat"/>) is floated to the top so the default/top selection
    /// matches yt-dlp's default pick even when the ranking would otherwise rank a higher-bitrate
    /// non-44.1 kHz format above it.
    /// </summary>
    public List<YtDlpFormat> AudioFormatsByPreference(string? recommendedFormatId) =>
        [
            .. AudioOnlyFormats
                .OrderByDescending(f => f.FormatId == recommendedFormatId)
                .ThenByDescending(f => f.AudioBitrate)
                .ThenByDescending(f => f.AudioChannels ?? 0)
                .ThenByDescending(f => f.CodecPreference)
                .ThenByDescending(f => f.FileSize ?? f.FileSizeApprox ?? 0)
                .ThenBy(f => f.FormatId, StringComparer.Ordinal),
        ];

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

    /// <summary>
    /// Codec preference rank used as a tiebreak when bitrate and channels are equal. Higher is
    /// better. Mirrors the relative ordering of yt-dlp's default acodec preference list
    /// (lossless first, then opus/aac, down to mp3 and unknown codecs).
    /// </summary>
    [JsonIgnore]
    public int CodecPreference =>
        (AudioCodec ?? string.Empty) switch
        {
            "flac" => 7,
            "alac" => 6,
            "opus" => 5,
            "mp4a.40.2" or "mp4a.40.5" or "mp4a.40.29" or "aac" => 4,
            "vorbis" => 3,
            "ac-3" or "a52" or "ec-3" => 2,
            "mp4a.40.34" or "mp3" => 1,
            _ => 0,
        };
}

/// <summary>
/// Source-generated serializer context for yt-dlp metadata. The snake_case naming policy lives
/// here, co-located with the DTO it describes, rather than on a distant call site.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(YtDlpVideoInfo))]
internal sealed partial class YtDlpJsonContext : JsonSerializerContext { }

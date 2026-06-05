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

    /// <summary>
    /// Full thumbnails array from yt-dlp. Entries may omit width/height (e.g. for auto-generated
    /// YouTube thumbnails); those entries are treated as dimension-unknown and skipped when
    /// selecting by aspect ratio.
    /// </summary>
    public List<YtDlpThumbnail> Thumbnails { get; init; } = [];

    [JsonIgnore]
    public List<YtDlpFormat> AudioOnlyFormats => [.. Formats.Where(f => f.IsAudioOnly)];

    /// <summary>
    /// Audio-only formats ordered best-first by quality so the picker reads top-down from the
    /// highest-quality option: descending bitrate, then channels, then a codec-preference
    /// tiebreak, then filesize, with a deterministic format-id final tiebreak. The recommended
    /// ("AUTO") pick (see <see cref="SelectBestAudioFormat"/>) is surfaced by the picker's AUTO
    /// tag in place and is deliberately NOT reordered to the top: the 44.1 kHz pick can sit just
    /// below a marginally higher-bitrate 48 kHz option, which is where the user expects to see it.
    /// </summary>
    public List<YtDlpFormat> AudioFormatsByPreference() =>
        [
            .. AudioOnlyFormats
                .OrderByDescending(f => f.AudioBitrate)
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

    // Selection-policy thresholds for SelectBestThumbnail. The square-shape definition itself lives
    // on YtDlpThumbnail.IsSquare.
    private const int SquareSizeCap = 1200;
    private const int TinySquareThreshold = 300;
    private const double DramaticallyLargerFactor = 3.0;

    /// <summary>
    /// Picks the best thumbnail URL from the <see cref="Thumbnails"/> array, preferring a square
    /// (aspect ratio within 0.95 to 1.05).
    ///
    /// Selection policy:
    ///   - Among thumbnails with known dimensions, identify square candidates (ratio 0.95 to 1.05).
    ///   - Prefer the largest square whose longest side is at or below <see cref="SquareSizeCap"/>
    ///     (1200 px). If all squares exceed the cap, take the smallest square instead.
    ///   - When the best square is tiny (longest side below <see cref="TinySquareThreshold"/>, 300 px)
    ///     AND the largest non-square thumbnail with known dimensions is at least
    ///     <see cref="DramaticallyLargerFactor"/> (3x) bigger on its longest side, the larger
    ///     non-square wins.
    ///   - When no square with known dimensions exists, or when the list is empty, fall back to
    ///     <see cref="Thumbnail"/> (today's single-best thumbnail from yt-dlp).
    ///
    /// Mirrors the "prefer X unless the alternative is materially better" idiom used in
    /// <see cref="SelectBestAudioFormat"/>.
    /// </summary>
    public string? SelectBestThumbnail()
    {
        var sized = Thumbnails.Where(t => t.IsSized).ToList();
        var squares = sized.Where(t => t.IsSquare).ToList();
        if (squares.Count == 0)
            return Thumbnail;

        // Largest square at or below the cap; if every square exceeds it, the smallest square.
        var bestSquare =
            squares.Where(t => t.LongestSide <= SquareSizeCap).MaxBy(t => t.LongestSide)
            ?? squares.MinBy(t => t.LongestSide)!;

        // A tiny square yields to a dramatically larger non-square.
        if (bestSquare.LongestSide < TinySquareThreshold)
        {
            var bestNonSquare = sized.Where(t => !t.IsSquare).MaxBy(t => t.LongestSide);
            if (
                bestNonSquare is { } nonSquare
                && nonSquare.LongestSide >= bestSquare.LongestSide * DramaticallyLargerFactor
            )
                return nonSquare.Url;
        }

        return bestSquare.Url;
    }
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
/// One entry from the yt-dlp <c>thumbnails</c> array. Width, height, and resolution are
/// optional because many auto-generated thumbnails (e.g. YouTube's hqdefault) omit them.
/// </summary>
public sealed record YtDlpThumbnail
{
    private const double SquareRatioMin = 0.95;
    private const double SquareRatioMax = 1.05;

    public string? Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public int? Preference { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Resolution { get; init; }

    /// <summary>True when both <see cref="Width"/> and <see cref="Height"/> are known.</summary>
    [JsonIgnore]
    public bool IsSized => Width.HasValue && Height.HasValue;

    /// <summary>Longest side in pixels, or 0 when dimensions are unknown.</summary>
    [JsonIgnore]
    public int LongestSide => Math.Max(Width ?? 0, Height ?? 0);

    /// <summary>True when the thumbnail is sized and roughly square (aspect 0.95 to 1.05).</summary>
    [JsonIgnore]
    public bool IsSquare =>
        IsSized && (double)Width!.Value / Height!.Value is >= SquareRatioMin and <= SquareRatioMax;
}

/// <summary>
/// Source-generated serializer context for yt-dlp metadata. The snake_case naming policy lives
/// here, co-located with the DTO it describes, rather than on a distant call site.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(YtDlpVideoInfo))]
[JsonSerializable(typeof(YtDlpThumbnail))]
internal sealed partial class YtDlpJsonContext : JsonSerializerContext { }

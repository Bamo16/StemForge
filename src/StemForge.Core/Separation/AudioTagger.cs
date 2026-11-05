using System.Globalization;
using TagLib;
using TFile = TagLib.File;

namespace StemForge.Core.Separation;

/// <summary>
/// Reads source-file metadata (tags + cover art) and writes them — along with separation
/// provenance — to output stem files using TagLibSharp. All operations are in-place: no
/// temp files, no re-encoding, no extra processes.
/// </summary>
public static class AudioTagger
{
    // About four seconds in all: long enough for a reader such as a key or BPM detector to let go.
    private static readonly TimeSpan[] ShareRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads audio stream properties (codec, bitrate, sample rate, duration) from a local
    /// audio file using TagLibSharp. Returns all nulls if the file is unreadable or the
    /// format is unsupported.
    /// </summary>
    public static (
        string? Codec,
        string? Bitrate,
        string? SampleRate,
        string? Duration
    ) ReadAudioProperties(string path)
    {
        try
        {
            using var f = TFile.Create(path);
            var props = f.Properties;

            var codec = CodecFromMimeType(f.MimeType);
            var bitrate = props.AudioBitrate > 0 ? $"{props.AudioBitrate} kb/s" : null;
            var sampleRate =
                props.AudioSampleRate > 0 ? $"{props.AudioSampleRate / 1000.0:F1} kHz" : null;
            var duration =
                props.Duration > TimeSpan.Zero
                    ? (
                        props.Duration.TotalHours >= 1
                            ? props.Duration.ToString(@"h\:mm\:ss")
                            : props.Duration.ToString(@"m\:ss")
                    )
                    : null;

            return (codec, bitrate, sampleRate, duration);
        }
        catch (Exception ex)
        {
            AppLogger.Debug(
                "tagger",
                $"Could not read audio properties from {Path.GetFileName(path)}: {ex.Message}"
            );
            return (null, null, null, null);
        }
    }

    private static string? CodecFromMimeType(string? mimeType) =>
        mimeType?.ToLowerInvariant() switch
        {
            "taglib/flac" => "FLAC",
            "taglib/mp3" => "MP3",
            "taglib/mp4" or "taglib/m4a" or "taglib/aac" => "AAC",
            "taglib/ogg" or "taglib/opus" => "Opus",
            "taglib/wav" => "WAV",
            "taglib/aiff" or "taglib/aif" => "AIFF",
            "taglib/wma" => "WMA",
            _ when mimeType is { Length: > 8 } => mimeType["taglib/".Length..].ToUpperInvariant(),
            _ => null,
        };

    /// <summary>
    /// Reads standard tags and embedded cover art from a local audio file. Returns null if the
    /// format is unsupported; throws <see cref="AudioTagException"/> if the file cannot be opened.
    /// </summary>
    public static SourceTagInfo? ReadFromFile(string path) => ReadFromFile(path, ShareRetryDelays);

    internal static SourceTagInfo? ReadFromFile(string path, IReadOnlyList<TimeSpan> retryDelays)
    {
        try
        {
            SourceTagInfo? info = null;
            WithShareRetry(path, retryDelays, () => info = Read(path));

            return info;
        }
        catch (Exception ex) when (ex is CorruptFileException or UnsupportedFormatException)
        {
            AppLogger.Debug(
                "tagger",
                $"Could not read tags from {Path.GetFileName(path)}: {ex.Message}"
            );
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AudioTagException(
                $"Could not read tags from {Path.GetFileName(path)}: {ex.Message}",
                ex
            );
        }
    }

    private static SourceTagInfo Read(string path)
    {
        using var f = TFile.Create(path);
        var pic = BestPicture(f.Tag.Pictures);
        var (sourceUrl, sourceCodec, sourceBitrateKbps, sourceFormatId) = ParseProvenance(
            f.Tag.Comment
        );
        return new SourceTagInfo
        {
            Title = NullIfEmpty(f.Tag.Title),
            Artist = NullIfEmpty(f.Tag.FirstPerformer),
            Album = NullIfEmpty(f.Tag.Album),
            Year = f.Tag.Year,
            CoverArtBytes = pic?.Data.Data,
            CoverArtMimeType = NullIfEmpty(pic?.MimeType) ?? "image/jpeg",
            SourceUrl = sourceUrl,
            SourceCodec = sourceCodec,
            SourceBitrateKbps = sourceBitrateKbps,
            SourceFormatId = sourceFormatId,
        };
    }

    /// <summary>
    /// Builds a <see cref="SourceTagInfo"/> from yt-dlp resolved metadata.
    /// Reads cover art bytes from the thumbnail file on disk (downloaded separately).
    /// </summary>
    public static SourceTagInfo FromYtDlpMetadata(YtDlpMetadata meta, string? thumbPath)
    {
        byte[]? artBytes = null;
        string mimeType = "image/jpeg";

        if (thumbPath is not null && System.IO.File.Exists(thumbPath))
        {
            try
            {
                artBytes = System.IO.File.ReadAllBytes(thumbPath);
                mimeType = GuessMimeType(thumbPath);
            }
            catch (Exception ex)
            {
                AppLogger.Debug("tagger", $"Could not read thumbnail bytes: {ex.Message}");
            }
        }

        return new SourceTagInfo
        {
            Title = NullIfEmpty(meta.DisplayTitle),
            Artist = NullIfEmpty(meta.Artist),
            CoverArtBytes = artBytes,
            CoverArtMimeType = mimeType,
            SourceUrl = NullIfEmpty(meta.SourceUrl),
            SourceCodec = NullIfEmpty(meta.SourceCodec),
            SourceBitrateKbps = meta.SourceBitrateKbps is > 0 ? meta.SourceBitrateKbps : null,
            SourceFormatId = NullIfEmpty(meta.FormatId),
        };
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes source metadata and provenance tags to <paramref name="filePath"/> in-place, retrying
    /// briefly while another process holds the file, then throwing <see cref="AudioTagException"/>.
    /// </summary>
    public static void ApplyToFile(
        string filePath,
        SourceTagInfo? sourceInfo,
        string? presetDescriptor,
        string toolVersion
    ) => ApplyToFile(filePath, sourceInfo, presetDescriptor, toolVersion, ShareRetryDelays);

    internal static void ApplyToFile(
        string filePath,
        SourceTagInfo? sourceInfo,
        string? presetDescriptor,
        string toolVersion,
        IReadOnlyList<TimeSpan> retryDelays
    )
    {
        try
        {
            WithShareRetry(
                filePath,
                retryDelays,
                () => Write(filePath, sourceInfo, presetDescriptor, toolVersion)
            );
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or CorruptFileException
                        or UnsupportedFormatException
            )
        {
            throw new AudioTagException(
                $"Could not write tags to {Path.GetFileName(filePath)}: {ex.Message}",
                ex
            );
        }
    }

    // Only fields present in sourceInfo are written; the files tagged here (fresh stems and
    // downloads) carry no tags of their own to preserve.
    private static void Write(
        string filePath,
        SourceTagInfo? sourceInfo,
        string? presetDescriptor,
        string toolVersion
    )
    {
        using var f = TFile.Create(filePath);

        if (sourceInfo is { Title: { Length: > 0 } title })
            f.Tag.Title = title;
        if (sourceInfo is { Artist: { Length: > 0 } artist })
            f.Tag.Performers = [artist];
        if (sourceInfo is { Album: { Length: > 0 } album })
            f.Tag.Album = album;
        if (sourceInfo is { Year: > 0 and var year })
            f.Tag.Year = year;

        if (sourceInfo is { CoverArtBytes: { Length: > 0 } art })
        {
            f.Tag.Pictures =
            [
                new Picture([.. art])
                {
                    Type = PictureType.FrontCover,
                    MimeType = sourceInfo.CoverArtMimeType,
                    Description = "Cover",
                },
            ];
        }

        // Provenance in the Comment field — human-readable, survives all formats.
        // Exact-source fields (URL/codec/bitrate/format-id) are appended for URL jobs;
        // local-file jobs carry none of them and degrade to the tool/model/date prefix.
        f.Tag.Comment = BuildProvenance(sourceInfo, presetDescriptor, toolVersion);

        f.Save();
    }

    /// <summary>
    /// Runs <paramref name="action"/>, retrying after each delay while it fails with an
    /// <see cref="IOException"/>, which is how a file another process holds open fails.
    /// </summary>
    private static void WithShareRetry(
        string path,
        IReadOnlyList<TimeSpan> retryDelays,
        Action action
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();

                return;
            }
            catch (IOException ex)
                when (attempt < retryDelays.Count
                    && ex is not (FileNotFoundException or DirectoryNotFoundException)
                )
            {
                AppLogger.Debug(
                    "tagger",
                    $"{Path.GetFileName(path)} is busy, retrying in {retryDelays[attempt].TotalMilliseconds:0} ms: {ex.Message}"
                );
                Thread.Sleep(retryDelays[attempt]);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the human-readable provenance string written to the Comment tag. Always carries
    /// the tool/preset/date prefix; appends exact-source fields (URL, codec, bitrate, format-id)
    /// only when present, so local-file jobs degrade gracefully without empty trailers.
    /// </summary>
    internal static string BuildProvenance(
        SourceTagInfo? sourceInfo,
        string? presetDescriptor,
        string toolVersion
    )
    {
        var parts = new List<string> { $"stemforge/{toolVersion}" };

        if (presetDescriptor is { Length: > 0 } preset)
            parts.Add($"preset: {preset}");

        parts.Add(
            $"date: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        );

        if (sourceInfo is { SourceUrl: { Length: > 0 } url })
            parts.Add($"source: {url}");
        if (sourceInfo is { SourceCodec: { Length: > 0 } codec })
            parts.Add($"codec: {codec}");
        if (sourceInfo is { SourceBitrateKbps: > 0 and var bitrate })
            parts.Add($"bitrate: {bitrate.ToString("0.#", CultureInfo.InvariantCulture)} kbps");
        if (sourceInfo is { SourceFormatId: { Length: > 0 } formatId })
            parts.Add($"format-id: {formatId}");

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Recovers the exact-source fields <see cref="BuildProvenance"/> wrote into the Comment
    /// tag, so a file produced by a prior download or separation keeps its original provenance
    /// when fed back in as a local-file input (e.g. download once, then separate on the saved
    /// path). Only parses comments this tool wrote (must start with "stemforge/"); anything
    /// else — including a plain stem with no source fields — degrades to all-null, which is
    /// correct since there is nothing to recover.
    /// </summary>
    private static (
        string? SourceUrl,
        string? SourceCodec,
        double? SourceBitrateKbps,
        string? SourceFormatId
    ) ParseProvenance(string? comment)
    {
        if (
            string.IsNullOrEmpty(comment)
            || !comment.StartsWith("stemforge/", StringComparison.Ordinal)
        )
            return (null, null, null, null);

        string? sourceUrl = null;
        string? sourceCodec = null;
        double? sourceBitrateKbps = null;
        string? sourceFormatId = null;

        foreach (var part in comment.Split(" | "))
        {
            if (part.StartsWith("source: ", StringComparison.Ordinal))
                sourceUrl = NullIfEmpty(part["source: ".Length..]);
            else if (part.StartsWith("codec: ", StringComparison.Ordinal))
                sourceCodec = NullIfEmpty(part["codec: ".Length..]);
            else if (
                part.StartsWith("bitrate: ", StringComparison.Ordinal)
                && part.EndsWith(" kbps", StringComparison.Ordinal)
            )
            {
                var digits = part["bitrate: ".Length..^" kbps".Length];
                if (
                    double.TryParse(
                        digits,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var bitrate
                    )
                )
                    sourceBitrateKbps = bitrate;
            }
            else if (part.StartsWith("format-id: ", StringComparison.Ordinal))
                sourceFormatId = NullIfEmpty(part["format-id: ".Length..]);
        }

        return (sourceUrl, sourceCodec, sourceBitrateKbps, sourceFormatId);
    }

    private static IPicture? BestPicture(IPicture[]? pictures) =>
        pictures is [var first, ..]
            ? pictures.FirstOrDefault(pic => pic is { Type: PictureType.FrontCover }, first)
            : null;

    private static string GuessMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg",
        };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Tags could not be read from or written to an audio file, even after retrying.</summary>
public sealed class AudioTagException(string message, Exception innerException)
    : IOException(message, innerException);

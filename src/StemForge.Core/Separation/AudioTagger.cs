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
    /// Reads standard tags and embedded cover art from a local audio file.
    /// Returns null if the file format is unsupported or the file is unreadable.
    /// </summary>
    public static SourceTagInfo? ReadFromFile(string path)
    {
        try
        {
            using var f = TFile.Create(path);
            var pic = BestPicture(f.Tag.Pictures);
            return new SourceTagInfo
            {
                Title = NullIfEmpty(f.Tag.Title),
                Artist = NullIfEmpty(f.Tag.FirstPerformer),
                Album = NullIfEmpty(f.Tag.Album),
                Year = f.Tag.Year,
                CoverArtBytes = pic?.Data.Data,
                CoverArtMimeType = NullIfEmpty(pic?.MimeType) ?? "image/jpeg",
            };
        }
        catch (Exception ex)
        {
            AppLogger.Debug(
                "tagger",
                $"Could not read tags from {Path.GetFileName(path)}: {ex.Message}"
            );
            return null;
        }
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
    /// Writes source metadata and provenance tags to <paramref name="filePath"/> in-place.
    /// Only fields that are present in <paramref name="sourceInfo"/> are written; existing
    /// tags in the file are not preserved (stems from the separator have none).
    /// </summary>
    public static void ApplyToFile(
        string filePath,
        SourceTagInfo? sourceInfo,
        string? presetDescriptor,
        string toolVersion
    )
    {
        try
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
        catch (Exception ex)
        {
            AppLogger.Warning(
                "tagger",
                $"Failed to tag {Path.GetFileName(filePath)}: {ex.Message}"
            );
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

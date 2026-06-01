using System.Globalization;
using StemForge.Models;
using StemForge.Services;
using TagLib;
using TFile = TagLib.File;

namespace StemForge.Helpers;

/// <summary>
/// Reads source-file metadata (tags + cover art) and writes them — along with separation
/// provenance — to output stem files using TagLibSharp. All operations are in-place: no
/// temp files, no re-encoding, no extra processes.
/// </summary>
public static class AudioTagger
{
    // ── Reading ───────────────────────────────────────────────────────────────

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
        string? modelDescriptor,
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
            var provenance =
                $"stemforge/{toolVersion}"
                + (modelDescriptor is { Length: > 0 } m ? $" | model: {m}" : string.Empty)
                + $" | date: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            f.Tag.Comment = provenance;

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

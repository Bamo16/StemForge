namespace StemForge.Core.Separation.Models;

public static class AudioFormatInfo
{
    // Format IDs that yt-dlp only returns when authenticated with a YouTube Premium account.
    // Source: empirical testing on 50+ videos comparing yt-dlp output with/without premium cookies.
    private static readonly HashSet<string> _ytPremiumFormatIds =
    [
        "141", // Premium bitrate AAC
        "250", // Premium bitrate Opus
        "774",
    ];

    /// <summary>Human-friendly codec name for a raw yt-dlp acodec value.</summary>
    public static string PrettyCodec(string? raw) =>
        raw switch
        {
            // MP4A object-type-indication codes — see ISO/IEC 14496-3
            "mp4a.40.2" => "AAC LC",
            "mp4a.40.5" => "HE-AAC",
            "mp4a.40.29" => "HE-AACv2",
            "mp4a.40.34" => "MP3",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "ac-3" or "a52" => "AC-3",
            "ec-3" => "E-AC-3",
            "flac" => "FLAC",
            _ when string.IsNullOrWhiteSpace(raw) => string.Empty,
            _ => raw,
        };

    /// <summary>True if the format requires YouTube Premium to obtain.</summary>
    public static bool IsYouTubePremium(string? formatId, string? extractor) =>
        formatId is not null
        && extractor is not null
        && extractor.Equals("youtube", StringComparison.OrdinalIgnoreCase)
        && _ytPremiumFormatIds.Contains(formatId);
}

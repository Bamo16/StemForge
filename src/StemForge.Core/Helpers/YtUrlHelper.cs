using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace StemForge.Core.Helpers;

/// <summary>
/// URL validation and normalisation for YouTube (and general http/https) sources.
/// Centralises the regex and logic previously duplicated across JobQueueService and call sites.
/// </summary>
public static partial class YtUrlHelper
{
    /// <summary>
    /// Returns true when <paramref name="input"/> is a recognisable YouTube video reference or a
    /// valid http/https URL. On success <paramref name="normalized"/> is either the canonical
    /// <c>https://music.youtube.com/watch?v=…</c> form (for YouTube) or the trimmed original URL.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        if (input?.Trim() is not { Length: > 0 } trimmed)
        {
            normalized = null;
            return false;
        }

        // YouTube video ID or URL → canonicalise to music.youtube.com
        if (YtVideoIdRegex.Match(trimmed).Groups["VideoId"] is { Success: true, Value: { } id })
        {
            normalized = $"https://music.youtube.com/watch?v={id}";
            return true;
        }

        // Any other valid http/https URL (yt-dlp supports many sites beyond YouTube)
        if (
            Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        )
        {
            normalized = trimmed;
            return true;
        }

        normalized = null;
        return false;
    }

    [GeneratedRegex(
        @"^(?:(?:(?:https?:\/\/)?(?:(?:www|music|m)\.)?)?(?:youtube\.com|youtu\.be)(?:\S*?(?:\?v=|\/)))?(?<VideoId>[0-9A-Za-z_-]{11})(?:[&?].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    )]
    private static partial Regex YtVideoIdRegex { get; }
}

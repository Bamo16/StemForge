namespace StemForge.Core.Downloading;

/// <summary>Facts about the cookie source a user configures for yt-dlp.</summary>
public static class CookieSource
{
    // Chromium-based browsers yt-dlp can name. On Windows, Chrome 127+ encrypts its cookies with a
    // key only Chrome can use, and the other Chromium browsers follow Chrome's scheme.
    private static readonly string[] ChromiumBrowsers =
    [
        "brave",
        "chrome",
        "chromium",
        "edge",
        "opera",
        "vivaldi",
        "whale",
    ];

    /// <summary>
    /// True when <paramref name="cookies"/> names a Chromium browser on Windows, whose cookies
    /// yt-dlp usually cannot read. Accepts yt-dlp's full form, e.g. <c>chrome+keyring:Profile 1</c>.
    /// </summary>
    public static bool IsLikelyUnreadable(string? cookies, PlatformInfo platform) =>
        platform.Os == OSKind.Windows
        && cookies is { Length: > 0 }
        && ChromiumBrowsers.Contains(
            cookies.Split(':', '+')[0].Trim(),
            StringComparer.OrdinalIgnoreCase
        );
}

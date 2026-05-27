using System.Text.RegularExpressions;

namespace StemForge.Helpers;

/// <summary>
/// Extracts a clean version string from the raw <c>--version</c> / <c>-version</c> output of
/// each tool. Per-tool patterns rather than one shared regex: the formats differ enough that
/// trying to be clever would either over-match (e.g. grabbing a copyright year as the version)
/// or under-match (e.g. failing on multi-line ffmpeg output).
///
/// Fallback is always the first trimmed line — degrades gracefully when a future build of any
/// tool changes its output rather than crashing or showing an empty string.
/// </summary>
public static class ToolVersionExtractor
{
    /// <summary>
    /// Extract a normalised version string (e.g. <c>0.11.8</c>, <c>8.0</c>) from the tool's
    /// raw version-command output.
    /// </summary>
    public static string Extract(string toolName, string rawOutput) =>
        toolName switch
        {
            "uv" => MatchOr(rawOutput, @"^uv\s+(\S+)", FirstLine(rawOutput)),
            "yt-dlp" => MatchOr(rawOutput, @"^\s*(\d+(?:\.\d+)+)", FirstLine(rawOutput)),
            "audio-separator" => MatchOr(rawOutput, @"(\d+(?:\.\d+)+)", FirstLine(rawOutput)),
            // \S+ rather than a dotted-numeric pattern because rolling autobuilds use
            // identifiers like "N-124653-g0ac3b00a18-20260526" with no dotted version.
            "ffmpeg" => MatchOr(rawOutput, @"ffmpeg version\s+(\S+)", FirstLine(rawOutput)),
            // deno --version emits three lines: "deno X.Y.Z (...)", "v8 ...", "typescript ...".
            // We just want the deno version.
            "deno" => MatchOr(rawOutput, @"^deno\s+(\S+)", FirstLine(rawOutput)),
            _ => FirstLine(rawOutput),
        };

    private static string MatchOr(string input, string pattern, string fallback)
    {
        var m = Regex.Match(input, pattern, RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : fallback;
    }

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();
}

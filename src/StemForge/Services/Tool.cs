using System.Text.RegularExpressions;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Declarative descriptor for one prerequisite tool. The single source of per-tool metadata,
/// replacing the per-tool switch arms and property quintuplets that were previously scattered
/// across detection, install, path-resolution, and UI code. Pure data: executable-path
/// resolution lives in <see cref="AppPaths"/>, install execution in the install orchestrator.
/// </summary>
public sealed record Tool(
    ToolKind Kind,
    string CliName,
    string VersionArg,
    Regex VersionPattern,
    bool IsRequired,
    InstallStrategy InstallStrategy
)
{
    /// <summary>
    /// Extracts a clean version string from raw <c>--version</c> output via
    /// <see cref="VersionPattern"/> (capture group 1), falling back to the first trimmed line
    /// so a future output-format change degrades gracefully instead of showing nothing.
    /// </summary>
    public string ExtractVersion(string rawOutput) =>
        VersionPattern.Match(rawOutput).Groups[1] is { Success: true, Value: { } version }
            ? version
            : rawOutput.Split('\n')[0].Trim();

    /// <summary>
    /// The on-disk filename of this tool's binary under a <see cref="BundledFetch"/>: the CLI
    /// name plus the platform's executable suffix (e.g. <c>ffmpeg.exe</c> on Windows,
    /// <c>ffmpeg</c> on Unix). Used for archive extraction and bundled-path resolution.
    /// </summary>
    public string BundledBinaryFileName(PlatformInfo platform) =>
        CliName + platform.ExecutableSuffix;
}

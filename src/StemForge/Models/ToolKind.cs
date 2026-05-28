namespace StemForge.Models;

/// <summary>
/// The closed set of prerequisite tools StemForge depends on. Adding a member is a code
/// change, not a configuration change. Order is required-first, then optional, so UI that
/// renders one row per tool in declaration order groups required tools at the top.
/// </summary>
public enum ToolKind
{
    Uv,
    AudioSeparator,
    Ffmpeg,
    Ytdlp,
    Deno,
}

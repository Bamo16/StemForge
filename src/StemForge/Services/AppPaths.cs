using StemForge.Extensions;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Single source of truth for every filesystem path StemForge needs at runtime.
/// Each property reads the user's configured override from <see cref="AppSettings"/>
/// and falls back to a sensible default — discovered shim path, bare exe name on PATH,
/// or a user-profile directory — so callers don't need to repeat the coalesce logic.
/// </summary>
public sealed class AppPaths(AppSettings settings)
{
    private readonly AppSettings _settings = settings;

    // ── Tool executables ──────────────────────────────────────────────────────

    /// <summary>Resolved path (or PATH-resolvable name) of a tool's binary, keyed by kind.</summary>
    public string PathFor(ToolKind kind) =>
        kind switch
        {
            ToolKind.Uv => Uv,
            ToolKind.AudioSeparator => AudioSeparator,
            ToolKind.Ytdlp => Ytdlp,
            ToolKind.Ffmpeg => Ffmpeg,
            ToolKind.Deno => Deno,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>Path or PATH-resolvable name of the uv binary. Prefers the known install location
    /// so callers work even when uv was just installed and its directory is not yet on PATH.</summary>
    public string Uv => OverrideFor(ToolKind.Uv) ?? (File.Exists(KnownUvPath) ? KnownUvPath : "uv");

    /// <summary>Path or PATH-resolvable name of the yt-dlp binary. Prefers user override, then the
    /// bundled binary downloaded on first run, and finally bare 'yt-dlp' on PATH.</summary>
    public string Ytdlp =>
        OverrideFor(ToolKind.Ytdlp) ?? (File.Exists(BundledYtdlp) ? BundledYtdlp : "yt-dlp");

    /// <summary>
    /// Path or PATH-resolvable name of the ffmpeg binary. Prefers the user's explicit
    /// override, then the bundled binary downloaded on first run, and finally falls back
    /// to bare 'ffmpeg' on PATH for users who already have a system install.
    /// </summary>
    public string Ffmpeg =>
        OverrideFor(ToolKind.Ffmpeg) ?? (File.Exists(BundledFfmpeg) ? BundledFfmpeg : "ffmpeg");

    /// <summary>
    /// Path or PATH-resolvable name of the audio-separator binary. If no user
    /// override is configured, prefers the uv-installed shim location and falls
    /// back to the bare PATH name.
    /// </summary>
    public string AudioSeparator =>
        OverrideFor(ToolKind.AudioSeparator)
        ?? (File.Exists(UvAudioSeparatorShim) ? UvAudioSeparatorShim : "audio-separator");

    // ── Directories ───────────────────────────────────────────────────────────

    /// <summary>Where stem outputs are written. Defaults to ~/Music/Stems.</summary>
    public string OutputDirectory => Override(_settings.OutputDirectory) ?? DefaultOutputDirectory;

    /// <summary>Where audio-separator looks for / stores model files. Defaults to LocalAppData.</summary>
    public string ModelsDirectory => Override(_settings.ModelsDirectory) ?? DefaultModelsDirectory;

    /// <summary>Cache directory for drum stems when DrumStemLocation is CacheOnly.</summary>
    public string DrumCacheDirectory =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("StemForge", "drum-cache");

    /// <summary>Directory holding bundled binaries that StemForge downloads on first run.</summary>
    public string BundledBinDir =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("StemForge", "bin");

    /// <summary>Path to the bundled ffmpeg binary inside <see cref="BundledBinDir"/>.</summary>
    public string BundledFfmpeg =>
        Path.Combine(BundledBinDir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

    /// <summary>
    /// Path or PATH-resolvable name of the deno binary. yt-dlp auto-discovers deno on PATH
    /// to solve YouTube's n-challenges; we bundle it so users don't have to install a JS
    /// runtime themselves.
    /// </summary>
    public string Deno =>
        OverrideFor(ToolKind.Deno) ?? (File.Exists(BundledDeno) ? BundledDeno : "deno");

    /// <summary>Path to the bundled deno binary inside <see cref="BundledBinDir"/>.</summary>
    public string BundledDeno =>
        Path.Combine(BundledBinDir, OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    /// <summary>Path to the bundled yt-dlp binary inside <see cref="BundledBinDir"/>.</summary>
    public string BundledYtdlp =>
        Path.Combine(BundledBinDir, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");

    // ── Defaults (exposed so the Settings UI can show placeholder text) ──────

    public static string DefaultOutputDirectory =>
        Environment.SpecialFolder.UserProfile.GetFolderPath("Music", "Stems");

    public static string DefaultModelsDirectory =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("audio-separator", "models");

    /// <summary>Python executable inside the uv-managed audio-separator tool environment.</summary>
    public string SeparationDriverPython =>
        File.Exists(UvAudioSeparatorPython) ? UvAudioSeparatorPython : "python";

    /// <summary>Path to the separation driver script, co-located with the app binary.</summary>
    public static string SeparationDriverScript =>
        Path.Combine(AppContext.BaseDirectory, "tools", "separator_driver.py");

    // uv installs itself to %USERPROFILE%\.local\bin on Windows. Probing this lets callers
    // use uv immediately after installation without requiring a PATH-refresh restart.
    // TODO v0.2.0: add Linux/macOS path (~/.local/bin/uv, no .exe).
    private static string KnownUvPath =>
        Environment.SpecialFolder.UserProfile.GetFolderPath(".local", "bin", "uv.exe");

    // TODO v0.2.0: Windows-only (Scripts\ + .exe). Linux/macOS uses bin/ with no extension.
    private static string UvAudioSeparatorShim =>
        Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "uv",
            "tools",
            "audio-separator",
            "Scripts",
            "audio-separator.exe"
        );

    // TODO v0.2.0: Windows-only (Scripts\ + .exe). Linux/macOS uses bin/ with no extension.
    private static string UvAudioSeparatorPython =>
        Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "uv",
            "tools",
            "audio-separator",
            "Scripts",
            "python.exe"
        );

    private string? OverrideFor(ToolKind kind) => Override(_settings.GetToolPathOverride(kind));

    private static string? Override(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

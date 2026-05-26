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

    /// <summary>Path or PATH-resolvable name of the uv binary.</summary>
    public string Uv => Override(_settings.UvPath) ?? "uv";

    /// <summary>Path or PATH-resolvable name of the yt-dlp binary.</summary>
    public string Ytdlp => Override(_settings.YtdlpPath) ?? "yt-dlp";

    /// <summary>
    /// Path or PATH-resolvable name of the ffmpeg binary. Prefers the user's explicit
    /// override, then the bundled binary downloaded on first run, and finally falls back
    /// to bare 'ffmpeg' on PATH for users who already have a system install.
    /// </summary>
    public string Ffmpeg =>
        Override(_settings.FfmpegPath) ?? (File.Exists(BundledFfmpeg) ? BundledFfmpeg : "ffmpeg");

    /// <summary>
    /// Path or PATH-resolvable name of the audio-separator binary. If no user
    /// override is configured, prefers the uv-installed shim location and falls
    /// back to the bare PATH name.
    /// </summary>
    public string AudioSeparator =>
        Override(_settings.AudioSeparatorPath)
        ?? (File.Exists(UvAudioSeparatorShim) ? UvAudioSeparatorShim : "audio-separator");

    // ── Directories ───────────────────────────────────────────────────────────

    /// <summary>Where stem outputs are written. Defaults to ~/Music/Stems.</summary>
    public string OutputDirectory => Override(_settings.OutputDirectory) ?? DefaultOutputDirectory;

    /// <summary>Where audio-separator looks for / stores model files. Defaults to LocalAppData.</summary>
    public string ModelsDirectory => Override(_settings.ModelsDirectory) ?? DefaultModelsDirectory;

    /// <summary>Cache directory for drum stems when DrumStemLocation is CacheOnly.</summary>
    public string DrumCacheDirectory =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("StemForge", "drum-cache");

    /// <summary>
    /// Directory holding bundled binaries that StemForge downloads on first run (currently:
    /// ffmpeg). Prepended to PATH by <see cref="ProcessRunner"/> for every child process, so
    /// tools that shell out to bare 'ffmpeg' find our binary without it ever being on the
    /// system PATH.
    /// </summary>
    public string BundledBinDir =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("StemForge", "bin");

    /// <summary>Path to the bundled ffmpeg binary inside <see cref="BundledBinDir"/>.</summary>
    public string BundledFfmpeg =>
        Path.Combine(BundledBinDir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

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

    private static string UvAudioSeparatorShim =>
        Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "uv",
            "tools",
            "audio-separator",
            "Scripts",
            "audio-separator.exe"
        );

    private static string UvAudioSeparatorPython =>
        Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "uv",
            "tools",
            "audio-separator",
            "Scripts",
            "python.exe"
        );

    private static string? Override(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

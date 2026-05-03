using StemForge.Extensions;

namespace StemForge.Models;

public sealed class AppSettings
{
    public GpuVariant GpuVariant { get; set; } = GpuVariant.Cpu;
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;
    public string ModelsDirectory { get; set; } = DefaultModelsDirectory;
    public string YtdlpPath
    {
        get => string.IsNullOrWhiteSpace(field) ? "yt-dlp" : field;
        set => field = value;
    }

    // Browser name for --cookies-from-browser (e.g. "firefox", "chrome", "edge"). Null = no cookies.
    public string? YtdlpCookiesFromBrowser { get; set; }

    // JS runtime for --js-runtime (e.g. "deno", "node"). Null = let yt-dlp auto-detect.
    public string? YtdlpJsRuntime { get; set; }
    public bool FirstRunComplete { get; set; } = false;
    public GpuVariant? InstalledVariant { get; set; }

    public static string DefaultOutputDirectory =>
        Environment.SpecialFolder.UserProfile.GetFolderPath("Music", "Stems");

    public static string DefaultModelsDirectory =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("audio-separator", "models");
}

using System.Text.Json;
using System.Text.Json.Serialization;
using StemForge.Extensions;

namespace StemForge.Models;

/// <summary>
/// User-configurable application settings, persisted as JSON in
/// %AppData%\StemForge\settings.json. Every property holds the user's
/// <i>intent</i> only — fall-back logic for unset paths lives in
/// <c>AppPaths</c>, not here.
/// </summary>
public sealed class AppSettings
{
    /// <summary>GPU compute variant the user prefers for new audio-separator installs.</summary>
    public GpuVariant GpuVariant { get; set; } = GpuVariant.Cpu;

    /// <summary>Variant actually installed on disk, recorded after a successful install.</summary>
    public GpuVariant? InstalledVariant { get; set; }

    /// <summary>True once the first-run setup wizard has been dismissed or completed.</summary>
    public bool FirstRunComplete { get; set; } = false;

    // ── Tool path overrides (null = use AppPaths default) ─────────────────────

    /// <summary>Override path to the uv binary. Null to use PATH.</summary>
    public string? UvPath { get; set; }

    /// <summary>Override path to the audio-separator binary. Null to use the uv shim or PATH.</summary>
    public string? AudioSeparatorPath { get; set; }

    /// <summary>Override path to the yt-dlp binary. Null to use PATH.</summary>
    public string? YtdlpPath { get; set; }

    /// <summary>Override path to the ffmpeg binary. Null to use PATH.</summary>
    public string? FfmpegPath { get; set; }

    // ── Directory overrides (null = use AppPaths default) ────────────────────

    /// <summary>Override directory for stem outputs. Null to use ~/Music/Stems.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Override directory for downloaded model files. Null to use LocalAppData.</summary>
    public string? ModelsDirectory { get; set; }

    // ── yt-dlp options ────────────────────────────────────────────────────────

    /// <summary>Browser name for --cookies-from-browser (e.g. "firefox", "chrome", "edge"). Null = no cookies.</summary>
    public string? YtdlpCookiesFromBrowser { get; set; }

    /// <summary>JS runtime for --js-runtime (e.g. "deno", "node"). Null = let yt-dlp auto-detect.</summary>
    public string? YtdlpJsRuntime { get; set; }

    // ── Logging ───────────────────────────────────────────────────────────────

    /// <summary>Max entries retained in the global Logs view ring buffer.</summary>
    public int MaxLogEntries { get; set; } = 2000;

    /// <summary>Max lines retained in each per-job log card on the Queue page.</summary>
    public int MaxJobLogLines { get; set; } = 500;

    // ── Audio output ──────────────────────────────────────────────────────────

    /// <summary>Default download format for URL-sourced audio (yt-dlp + ffmpeg).</summary>
    public AudioFormat DefaultAudioFormat { get; set; } = AudioFormat.Flac;

    // ── Drum extraction ───────────────────────────────────────────────────────

    /// <summary>audio-separator model used for per-job drum stem extraction.</summary>
    public string DrumExtractionModel { get; set; } = "htdemucs_ft.yaml";

    /// <summary>Where the drum stem file is written when drum extraction is enabled.</summary>
    public DrumStemLocation DrumStemLocation { get; set; } = DrumStemLocation.WithStems;

    // ── Persistence ───────────────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new();
            }
        }
        catch
        { /* corrupt settings — start fresh */
        }
        return new();
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(this, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }

    private static readonly string _settingsPath =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("StemForge", "settings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

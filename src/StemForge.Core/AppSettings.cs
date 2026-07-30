using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Core;

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

    // ── Tool path overrides (missing/null entry = use AppPaths default) ───────

    /// <summary>
    /// User overrides for tool executable paths, keyed by <see cref="ToolKind"/>. A missing or
    /// null entry means "use the AppPaths default". Use <see cref="GetToolPathOverride"/> /
    /// <see cref="SetToolPathOverride"/> rather than mutating directly.
    /// </summary>
    public Dictionary<ToolKind, string?> ToolPathOverrides { get; set; } = [];

    public string? GetToolPathOverride(ToolKind kind) => ToolPathOverrides.GetValueOrDefault(kind);

    public void SetToolPathOverride(ToolKind kind, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            ToolPathOverrides.Remove(kind);
        else
            ToolPathOverrides[kind] = value;
    }

    // Legacy per-tool path properties (pre-catalog). Deserialize-only: drained into
    // ToolPathOverrides by MigrateLegacyToolPaths on load, then written as null (and dropped
    // from output by WhenWritingNull). Kept so settings files written by v0.1.0 still migrate.
    public string? UvPath { get; set; }
    public string? AudioSeparatorPath { get; set; }
    public string? YtdlpPath { get; set; }
    public string? FfmpegPath { get; set; }
    public string? DenoPath { get; set; }

    // ── Directory overrides (null = use AppPaths default) ────────────────────

    /// <summary>Override directory for stem outputs. Null to use ~/Music/Stems.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Override directory for downloaded model files. Null to use LocalAppData.</summary>
    public string? ModelsDirectory { get; set; }

    // ── yt-dlp options ────────────────────────────────────────────────────────

    /// <summary>Browser name for --cookies-from-browser (e.g. "firefox", "chrome", "edge"). Null = no cookies.</summary>
    public string? YtdlpCookiesFromBrowser { get; set; }

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
                var settings =
                    JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings)
                    ?? new();
                settings.MigrateLegacyToolPaths();
                return settings;
            }
        }
        catch
        { /* corrupt settings — start fresh */
        }
        return new();
    }

    /// <summary>
    /// One-shot migration of the pre-catalog per-tool path properties into
    /// <see cref="ToolPathOverrides"/>. Drains each non-null legacy value (without clobbering an
    /// existing dictionary entry) and clears the legacy property so the next save writes only
    /// the new shape. Idempotent: a no-op once the legacy properties are null.
    /// </summary>
    internal void MigrateLegacyToolPaths()
    {
        Drain(ToolKind.Uv, UvPath);
        Drain(ToolKind.AudioSeparator, AudioSeparatorPath);
        Drain(ToolKind.Ytdlp, YtdlpPath);
        Drain(ToolKind.Ffmpeg, FfmpegPath);
        Drain(ToolKind.Deno, DenoPath);
        UvPath = AudioSeparatorPath = YtdlpPath = FfmpegPath = DenoPath = null;

        void Drain(ToolKind kind, string? legacy)
        {
            if (!string.IsNullOrWhiteSpace(legacy) && !ToolPathOverrides.ContainsKey(kind))
                ToolPathOverrides[kind] = legacy;
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
        await File.WriteAllTextAsync(_settingsPath, json);
    }

    private static readonly string _settingsPath =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("StemForge", "settings.json");
}

/// <summary>
/// Source-generated serializer context for <see cref="AppSettings"/>. Carries the settings.json
/// serialization contract (indented output, enums as strings, nulls omitted) co-located with the
/// type rather than at the distant call site. <c>UseStringEnumConverter</c> keeps enums such as
/// <see cref="DrumStemLocation"/> and <see cref="GpuVariant"/> serializing by name.
/// </summary>
[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext { }

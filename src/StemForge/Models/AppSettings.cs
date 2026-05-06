using System.Text.Json;
using System.Text.Json.Serialization;
using StemForge.Extensions;

namespace StemForge.Models;

public sealed class AppSettings
{
    public GpuVariant GpuVariant { get; set; } = GpuVariant.Cpu;
    public string OutputDirectory
    {
        get => string.IsNullOrWhiteSpace(field) ? DefaultOutputDirectory : field;
        set;
    }
    public string ModelsDirectory
    {
        get => string.IsNullOrWhiteSpace(field) ? DefaultModelsDirectory : field;
        set;
    }
    public string YtdlpPath
    {
        get => string.IsNullOrWhiteSpace(field) ? "yt-dlp" : field;
        set;
    }

    /// <summary>Browser name for --cookies-from-browser (e.g. "firefox", "chrome", "edge"). Null = no cookies.</summary>
    public string? YtdlpCookiesFromBrowser { get; set; }

    /// <summary>JS runtime for --js-runtime (e.g. "deno", "node"). Null = let yt-dlp auto-detect.</summary>
    public string? YtdlpJsRuntime { get; set; }
    public bool FirstRunComplete { get; set; } = false;
    public GpuVariant? InstalledVariant { get; set; }

    /// <summary>Max entries retained in the global Logs view ring buffer.</summary>
    public int MaxLogEntries { get; set; } = 2000;

    /// <summary>Max lines retained in each per-job log card on the Queue page.</summary>
    public int MaxJobLogLines { get; set; } = 500;

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
    };

    private static string DefaultOutputDirectory =>
        Environment.SpecialFolder.UserProfile.GetFolderPath("Music", "Stems");

    private static string DefaultModelsDirectory =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("audio-separator", "models");
}

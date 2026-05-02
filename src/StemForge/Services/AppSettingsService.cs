using System.Text.Json;
using System.Text.Json.Serialization;
using StemForge.Extensions;
using StemForge.Models;

namespace StemForge.Services;

public sealed class AppSettingsService
{
    private static readonly string SettingsPath =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("StemForge", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Current { get; private set; } = new();

    public static AppSettingsService Load()
    {
        var svc = new AppSettingsService();
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                svc.Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
            }
        }
        catch
        { /* corrupt settings — start fresh */
        }
        return svc;
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using StemForge.Core.Extensions;
using StemForge.Core.Models;

namespace StemForge.Core.Services;

public sealed partial class UserPresetService
{
    private const string FileName = "user_presets.json";

    // Current (Roaming) location, sharing the same root as settings.json so all user-authored
    // config lives together.
    private static string FilePath =>
        Environment.SpecialFolder.ApplicationData.GetFolderPath("StemForge", FileName);

    // Legacy (Local) location written by v0.1.x. Migrated to Roaming on load.
    private static string LegacyFilePath =>
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("StemForge", FileName);

    private readonly string _filePath;

    public ObservableCollection<Preset> Presets { get; } = [];

    public UserPresetService()
        : this(FilePath) { }

    // Path-injecting constructor for tests that exercise Add/Remove/Save without touching the real
    // Roaming location.
    internal UserPresetService(string filePath) => _filePath = filePath;

    public static UserPresetService Load() => Load(FilePath, LegacyFilePath);

    // Path-injecting overload so the migrate-on-load path can be exercised without touching the
    // real Roaming/Local folders.
    internal static UserPresetService Load(string roamingPath, string legacyPath)
    {
        MigrateLegacyLocation(roamingPath, legacyPath);

        var svc = new UserPresetService(roamingPath);
        try
        {
            if (File.Exists(roamingPath))
            {
                var json = File.ReadAllText(roamingPath);
                var dtos = JsonSerializer.Deserialize(
                    json,
                    PresetJsonContext.Default.ListPresetDto
                );
                if (dtos is not null)
                    foreach (var dto in dtos)
                        svc.Presets.Add(dto.ToPreset());
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning(
                nameof(UserPresetService),
                $"Could not load user presets: {ex.Message}"
            );
        }
        return svc;
    }

    /// <summary>
    /// Reconciles the presets file between the legacy Local location and the current Roaming
    /// location so the two never coexist indefinitely. Roaming is the single source of truth:
    /// <list type="bullet">
    /// <item>No legacy file: nothing to do.</item>
    /// <item>Legacy file but no Roaming file: move (migrate) the legacy file to Roaming.</item>
    /// <item>Both present: Roaming wins, so the orphaned legacy file is deleted. No preset loss,
    /// because Roaming already holds the authoritative copy.</item>
    /// </list>
    /// Idempotent and a no-op once reconciled. Mirrors the migrate-on-load pattern in
    /// <see cref="StemForge.Models.AppSettings.MigrateLegacyToolPaths"/>.
    /// </summary>
    internal static void MigrateLegacyLocation(string roamingPath, string legacyPath)
    {
        try
        {
            if (!File.Exists(legacyPath))
                return;

            if (File.Exists(roamingPath))
            {
                // Roaming is authoritative; drop the stale Local duplicate so the two do not
                // coexist. Nothing is lost because Roaming already holds the presets.
                File.Delete(legacyPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(roamingPath)!);
            File.Move(legacyPath, roamingPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(
                nameof(UserPresetService),
                $"Could not reconcile user presets between Local and Roaming: {ex.Message}"
            );
        }
    }

    public void Add(Preset preset)
    {
        Presets.Add(preset);
        Save();
    }

    public void Remove(string id)
    {
        var match = Presets.FirstOrDefault(p => p.Id == id);
        if (match is not null)
        {
            Presets.Remove(match);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            var dtos = Presets.Select(PresetDto.FromPreset).ToList();
            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(dtos, PresetJsonContext.Default.ListPresetDto)
            );
        }
        catch (Exception ex)
        {
            AppLogger.Warning(
                nameof(UserPresetService),
                $"Could not save user presets: {ex.Message}"
            );
        }
    }

    private sealed class PresetDto
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Category { get; set; } = "Other";
        public string Description { get; set; } = "";
        public int ModelCount { get; set; } = 1;
        public string Vram { get; set; } = "";
        public string Mode { get; set; } = "SingleModel";
        public string? PrimaryModel { get; set; }
        public string? EnsembleAlgorithm { get; set; }
        public List<string>? ExtraModels { get; set; }
        public List<double>? EnsembleWeights { get; set; }

        public Preset ToPreset() =>
            new(
                Id,
                Label,
                Enum.TryParse<PresetCategory>(Category, out var cat) ? cat : PresetCategory.Other,
                Description,
                ModelCount,
                Vram,
                Models: null,
                Mode: Enum.TryParse<SeparationMode>(Mode, out var mode)
                    ? mode
                    : SeparationMode.SingleModel,
                PrimaryModel: PrimaryModel,
                EnsembleAlgorithm: EnsembleAlgorithm,
                ExtraModels: ExtraModels,
                EnsembleWeights: EnsembleWeights
            );

        public static PresetDto FromPreset(Preset p) =>
            new()
            {
                Id = p.Id,
                Label = p.Label,
                Category = p.Category.ToString(),
                Description = p.Description,
                ModelCount = p.ModelCount,
                Vram = p.Vram,
                Mode = p.Mode.ToString(),
                PrimaryModel = p.PrimaryModel,
                EnsembleAlgorithm = p.EnsembleAlgorithm,
                ExtraModels = p.ExtraModels?.ToList(),
                EnsembleWeights = p.EnsembleWeights?.ToList(),
            };
    }

    /// <summary>
    /// Source-generated serializer context for the preset file. Carries the read/write contract
    /// (case-insensitive property matching, indented output) co-located with <see cref="PresetDto"/>.
    /// Nested so it can see the private DTO type.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(List<PresetDto>))]
    private sealed partial class PresetJsonContext : JsonSerializerContext { }
}

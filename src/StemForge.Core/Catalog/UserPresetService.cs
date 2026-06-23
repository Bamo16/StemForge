using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Core.Catalog;

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

    /// <summary>
    /// One persisted <see cref="PresetStep"/>: where it reads its audio, the model-or-ensemble to
    /// run, the ensemble algorithm (for two or more models), and the keep set. This is the unit the
    /// on-disk schema is built around so chained (multi-step) presets need no future migration.
    /// </summary>
    private sealed class StepDto
    {
        public string Input { get; set; } = nameof(StepInput.Source);
        public List<string> Models { get; set; } = [];
        public string? Algorithm { get; set; }
        public List<string>? KeepSet { get; set; }

        public PresetStep ToStep() =>
            new(
                Enum.TryParse<StepInput>(Input, out var input) ? input : StepInput.Source,
                Models,
                Algorithm,
                KeepSet
            );

        public static StepDto FromStep(PresetStep s) =>
            new()
            {
                Input = s.Input.ToString(),
                Models = s.Models.ToList(),
                Algorithm = s.Algorithm,
                KeepSet = s.KeepSet?.ToList(),
            };
    }

    /// <summary>
    /// Persisted shape of a user <see cref="Preset"/>. Carries the preset's metadata plus its
    /// ordered <see cref="Steps"/> list. The flat model/algorithm fields (<see cref="PrimaryModel"/>,
    /// <see cref="ExtraModels"/>, <see cref="EnsembleAlgorithm"/>, <see cref="Mode"/>) are the legacy
    /// v0.2.x schema: still read when <see cref="Steps"/> is absent and migrated into a single step,
    /// but no longer written.
    /// </summary>
    private sealed class PresetDto
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Category { get; set; } = "Other";
        public string Description { get; set; } = "";
        public int ModelCount { get; set; } = 1;
        public string Vram { get; set; } = "";

        // Current schema: an ordered steps list. Single-step today.
        public List<StepDto>? Steps { get; set; }

        // Legacy (v0.2.x) flat schema. Read-only fallback when Steps is absent; never written.
        public string? Mode { get; set; }
        public string? PrimaryModel { get; set; }
        public string? EnsembleAlgorithm { get; set; }
        public List<string>? ExtraModels { get; set; }
        public List<double>? EnsembleWeights { get; set; }

        public Preset ToPreset()
        {
            var category = Enum.TryParse<PresetCategory>(Category, out var cat)
                ? cat
                : PresetCategory.Other;

            // Current schema: an explicit steps list is authoritative.
            if (Steps is { Count: > 0 } stepDtos)
            {
                var steps = stepDtos.Select(s => s.ToStep()).ToList();
                var first = steps[0];
                var mode =
                    first.Models.Count >= 2 ? SeparationMode.CustomEnsemble
                    : first.Models.Count == 1 ? SeparationMode.SingleModel
                    : SeparationMode.BuiltinPreset;

                // Re-expose the single step through the flat accessors so in-memory consumers and the
                // pipeline see the same shape as a freshly created preset.
                return new Preset(
                    Id,
                    Label,
                    category,
                    Description,
                    ModelCount,
                    Vram,
                    Models: null,
                    Mode: mode,
                    PrimaryModel: first.Models.Count > 0 ? first.Models[0] : null,
                    EnsembleAlgorithm: first.Algorithm,
                    ExtraModels: first.Models.Count > 1 ? first.Models.Skip(1).ToList() : null,
                    EnsembleWeights: EnsembleWeights,
                    Steps: steps
                );
            }

            // Legacy flat schema: migrate into a single-step preset. The flat parameters flow into
            // the synthesized step via the Preset constructor, so no data is lost.
            return new Preset(
                Id,
                Label,
                category,
                Description,
                ModelCount,
                Vram,
                Models: null,
                Mode: Enum.TryParse<SeparationMode>(Mode, out var legacyMode)
                    ? legacyMode
                    : SeparationMode.SingleModel,
                PrimaryModel: PrimaryModel,
                EnsembleAlgorithm: EnsembleAlgorithm,
                ExtraModels: ExtraModels,
                EnsembleWeights: EnsembleWeights
            );
        }

        public static PresetDto FromPreset(Preset p) =>
            new()
            {
                Id = p.Id,
                Label = p.Label,
                Category = p.Category.ToString(),
                Description = p.Description,
                ModelCount = p.ModelCount,
                Vram = p.Vram,
                Steps = p.Steps.Select(StepDto.FromStep).ToList(),
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

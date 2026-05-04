using System.Collections.ObjectModel;
using System.Text.Json;
using StemForge.Extensions;
using StemForge.Models;

namespace StemForge.Services;

public sealed class UserPresetService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(
            Environment.SpecialFolder.LocalApplicationData.GetFolderPath(["StemForge"]),
            "user_presets.json"
        );

    public ObservableCollection<Preset> Presets { get; } = [];

    public static UserPresetService Load()
    {
        var svc = new UserPresetService();
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var dtos = JsonSerializer.Deserialize<List<PresetDto>>(json, JsonOpts);
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
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var dtos = Presets.Select(PresetDto.FromPreset).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dtos, JsonOpts));
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
                Enum.TryParse<SeparationMode>(Mode, out var mode)
                    ? mode
                    : SeparationMode.SingleModel,
                PrimaryModel,
                EnsembleAlgorithm,
                ExtraModels,
                EnsembleWeights
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
}

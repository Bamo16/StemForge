using StemForge.Models;

namespace StemForge.Tests.Models;

public sealed class JobRecordTests
{
    [Fact]
    public void PresetSummary_OnePreset_NoDrums_ReturnsPresetLabel()
    {
        var record = Build(presets: [MakePreset("vocals", "Vocals")], extractDrums: false);
        Assert.Equal("Vocals", record.PresetSummary);
    }

    [Fact]
    public void PresetSummary_TwoPresets_NoDrums_ReturnsTwoPresetsLabel()
    {
        var record = Build(
            presets: [MakePreset("vocals", "Vocals"), MakePreset("inst", "Instrumental")],
            extractDrums: false
        );
        Assert.Equal("2 presets", record.PresetSummary);
    }

    [Fact]
    public void PresetSummary_OnePreset_WithDrums_ReturnsOnePresetPlusDrums()
    {
        var record = Build(presets: [MakePreset("vocals", "Vocals")], extractDrums: true);
        Assert.Equal("1 preset + Drums", record.PresetSummary);
    }

    [Fact]
    public void PresetSummary_TwoPresets_WithDrums_ReturnsTwoPresetsPlusDrums()
    {
        var record = Build(
            presets: [MakePreset("vocals", "Vocals"), MakePreset("inst", "Instrumental")],
            extractDrums: true
        );
        Assert.Equal("2 presets + Drums", record.PresetSummary);
    }

    private static JobRecord Build(IReadOnlyList<Preset> presets, bool extractDrums) =>
        new(
            Id: Guid.NewGuid(),
            InputFilePath: @"C:\audio\track.flac",
            SourceUrl: null,
            Presets: presets,
            OutputDir: @"C:\output",
            ModelsDir: @"C:\models",
            ExtractDrums: extractDrums
        );

    private static Preset MakePreset(string id, string label) =>
        new(
            Id: id,
            Label: label,
            Category: PresetCategory.Vocals,
            Description: "",
            ModelCount: 1,
            Vram: ""
        );
}

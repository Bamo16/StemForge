namespace StemForge.Tests.Models;

public sealed class PresetTests
{
    [Fact]
    public void AllModels_ModelsListHasEntries_ReturnsThatList()
    {
        var preset = Build(models: ["a.onnx", "b.onnx"]);
        Assert.Equal(["a.onnx", "b.onnx"], preset.AllModels);
    }

    [Fact]
    public void AllModels_ModelsNull_PrimaryWithExtras_ReturnsPrimaryPlusExtras()
    {
        var preset = Build(
            models: null,
            primaryModel: "primary.onnx",
            extraModels: ["extra1.onnx", "extra2.onnx"]
        );
        Assert.Equal(["primary.onnx", "extra1.onnx", "extra2.onnx"], preset.AllModels);
    }

    [Fact]
    public void AllModels_ModelsNull_PrimaryWithNoExtras_ReturnsSingletonList()
    {
        var preset = Build(models: null, primaryModel: "primary.onnx", extraModels: null);
        Assert.Equal(["primary.onnx"], preset.AllModels);
    }

    [Fact]
    public void AllModels_ModelsNullAndPrimaryNull_ReturnsEmpty()
    {
        var preset = Build(models: null, primaryModel: null);
        Assert.Empty(preset.AllModels);
    }

    [Fact]
    public void DisplayName_VocalsBuiltinPreset_QualifiesWithVocal()
    {
        var preset = BuiltinPreset("acapella", "Full", PresetCategory.Vocals);
        Assert.Equal("Vocal - Full", preset.DisplayName);
    }

    [Fact]
    public void DisplayName_InstrumentalsBuiltinPreset_QualifiesWithInstrumental()
    {
        var preset = BuiltinPreset("instrumental_full", "Full", PresetCategory.Instrumentals);
        Assert.Equal("Instrumental - Full", preset.DisplayName);
    }

    [Fact]
    public void DisplayName_Karaoke_IsKaraoke()
    {
        var preset = BuiltinPreset("karaoke", "Karaoke Mix", PresetCategory.Vocals);
        Assert.Equal("Karaoke", preset.DisplayName);
    }

    [Fact]
    public void DisplayName_OtherCategory_QualifiesWithCategoryName()
    {
        var preset = BuiltinPreset("bass_iso", "Isolated", PresetCategory.Bass);
        Assert.Equal("Bass - Isolated", preset.DisplayName);
    }

    [Fact]
    public void DisplayName_CustomMode_UsesLabelAsIs()
    {
        var preset = new Preset(
            Id: "custom-1",
            Label: "My Ensemble",
            Category: PresetCategory.Vocals,
            Description: "A custom preset",
            ModelCount: 1,
            Vram: "4 GB",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "model.onnx"
        );
        Assert.Equal("My Ensemble", preset.DisplayName);
    }

    [Fact]
    public void DrumExtraction_BuildsSingleModelDrumPreset_WithDisplayName()
    {
        var preset = Preset.DrumExtraction("htdemucs_ft.yaml");

        Assert.Equal(PresetCategory.Drums, preset.Category);
        Assert.Equal(SeparationMode.SingleModel, preset.Mode);
        Assert.Equal("htdemucs_ft.yaml", preset.PrimaryModel);
        Assert.Equal(["htdemucs_ft.yaml"], preset.AllModels);
        Assert.Equal("Drums - htdemucs_ft", preset.DisplayName);
    }

    private static Preset BuiltinPreset(string id, string label, PresetCategory category) =>
        new(
            Id: id,
            Label: label,
            Category: category,
            Description: "A test preset",
            ModelCount: 1,
            Vram: "4 GB",
            Models: ["model.onnx"]
        );

    private static Preset Build(
        IReadOnlyList<string>? models = null,
        string? primaryModel = null,
        IReadOnlyList<string>? extraModels = null
    ) =>
        new(
            Id: "test-preset",
            Label: "Test Preset",
            Category: PresetCategory.Vocals,
            Description: "A test preset",
            ModelCount: models?.Count ?? (primaryModel is null ? 0 : 1 + (extraModels?.Count ?? 0)),
            Vram: "4 GB",
            Models: models,
            PrimaryModel: primaryModel,
            ExtraModels: extraModels
        );
}

using StemForge.Models;

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

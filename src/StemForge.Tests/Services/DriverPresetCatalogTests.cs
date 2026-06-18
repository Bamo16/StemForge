using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Tests for mapping the driver's live preset catalog into <see cref="Preset"/> models. These cover
/// the category inference, label trimming, and field mapping that previously lived in
/// SeparatorDriverService.
/// </summary>
public sealed class DriverPresetCatalogTests
{
    // ── InferCategory ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("vocal_full", PresetCategory.Vocals)]
    [InlineData("vocal_some_preset", PresetCategory.Vocals)]
    [InlineData("instrumental_basic", PresetCategory.Instrumentals)]
    [InlineData("instrumental_low_res", PresetCategory.Instrumentals)]
    [InlineData("karaoke", PresetCategory.Instrumentals)]
    [InlineData("drums_only", PresetCategory.Other)]
    [InlineData("something_random", PresetCategory.Other)]
    public void InferCategory_ReturnsExpectedCategory(string id, PresetCategory expected) =>
        Assert.Equal(expected, DriverPresetCatalog.InferCategory(id));

    // ── StripCategoryPrefix ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Vocal Full", PresetCategory.Vocals, "Full")]
    [InlineData("vocal full", PresetCategory.Vocals, "full")]
    [InlineData("Instrumental Basic Separation", PresetCategory.Instrumentals, "Basic Separation")]
    [InlineData("something_else", PresetCategory.Other, "something_else")]
    [InlineData("no match here", PresetCategory.Vocals, "no match here")]
    public void StripCategoryPrefix_ReturnsExpectedLabel(
        string name,
        PresetCategory category,
        string expected
    ) => Assert.Equal(expected, DriverPresetCatalog.StripCategoryPrefix(name, category));

    // ── ToPresets ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToPresets_ValidEntry_PopulatesPresetFields()
    {
        var presets = DriverPresetCatalog.ToPresets(
            new Dictionary<string, DriverPresetEntry>
            {
                ["vocal_full"] = new()
                {
                    Name = "Vocal Full",
                    Description = "High quality vocal separation",
                    Models = ["model_a.onnx", "model_b.onnx"],
                },
            }
        );

        var p = Assert.Single(presets);
        Assert.Equal("vocal_full", p.Id);
        Assert.Equal("Full", p.Label);
        Assert.Equal(PresetCategory.Vocals, p.Category);
        Assert.Equal("High quality vocal separation", p.Description);
        Assert.Equal(["model_a.onnx", "model_b.onnx"], p.AllModels);
    }

    [Fact]
    public void ToPresets_MultipleEntries_MapsAll()
    {
        var presets = DriverPresetCatalog.ToPresets(
            new Dictionary<string, DriverPresetEntry>
            {
                ["vocal_full"] = new() { Name = "Vocal Full", Models = ["v_model.onnx"] },
                ["instrumental_basic"] = new()
                {
                    Name = "Instrumental Basic",
                    Models = ["i_model.onnx"],
                },
            }
        );

        Assert.Equal(2, presets.Count);
        Assert.Contains(presets, p => p.Id == "vocal_full" && p.Category == PresetCategory.Vocals);
        Assert.Contains(
            presets,
            p => p.Id == "instrumental_basic" && p.Category == PresetCategory.Instrumentals
        );
    }

    [Fact]
    public void ToPresets_EmptyNameField_FallsBackToId()
    {
        var presets = DriverPresetCatalog.ToPresets(
            new Dictionary<string, DriverPresetEntry>
            {
                ["some_preset"] = new() { Name = "", Models = ["m.onnx"] },
            }
        );

        Assert.Equal("some_preset", Assert.Single(presets).Label);
    }

    [Fact]
    public void ToPresets_EmptyCatalog_ReturnsEmptyList() =>
        Assert.Empty(DriverPresetCatalog.ToPresets(new Dictionary<string, DriverPresetEntry>()));

    [Fact]
    public void ToPresets_CarriesAlgorithmIntoEnsembleAlgorithm()
    {
        var presets = DriverPresetCatalog.ToPresets(
            new Dictionary<string, DriverPresetEntry>
            {
                ["vocal_full"] = new()
                {
                    Name = "Vocal Full",
                    Models = ["model_a.onnx"],
                    Algorithm = "max_fft",
                },
            }
        );

        Assert.Equal("max_fft", Assert.Single(presets).EnsembleAlgorithm);
    }

    [Fact]
    public void ToPresets_MissingAlgorithm_LeavesEnsembleAlgorithmNull()
    {
        var presets = DriverPresetCatalog.ToPresets(
            new Dictionary<string, DriverPresetEntry>
            {
                ["vocal_full"] = new() { Name = "Vocal Full", Models = ["model_a.onnx"] },
            }
        );

        Assert.Null(Assert.Single(presets).EnsembleAlgorithm);
    }

    [Fact]
    public void ToPresets_BlankAlgorithm_LeavesEnsembleAlgorithmNull()
    {
        var presets = DriverPresetCatalog.ToPresets(
            new Dictionary<string, DriverPresetEntry>
            {
                ["vocal_full"] = new()
                {
                    Name = "Vocal Full",
                    Models = ["model_a.onnx"],
                    Algorithm = "   ",
                },
            }
        );

        Assert.Null(Assert.Single(presets).EnsembleAlgorithm);
    }
}

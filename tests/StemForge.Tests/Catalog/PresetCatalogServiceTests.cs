namespace StemForge.Tests.Catalog;

/// <summary>
/// Tests for parsing the torch-free <c>list_presets.py</c> JSON into <see cref="Preset"/> models,
/// covering category inference, label trimming, and field mapping.
/// </summary>
public sealed class PresetCatalogServiceTests
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
        Assert.Equal(expected, PresetCatalogService.InferCategory(id));

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
    ) => Assert.Equal(expected, PresetCatalogService.StripCategoryPrefix(name, category));

    // ── ParsePresets ──────────────────────────────────────────────────────────

    [Fact]
    public void ParsePresets_ValidEntry_PopulatesPresetFields()
    {
        var presets = PresetCatalogService.ParsePresets(
            """
            {"vocal_full":{"name":"Vocal Full","description":"High quality vocal separation","models":["model_a.onnx","model_b.onnx"],"algorithm":"max_fft"}}
            """
        );

        var p = Assert.Single(presets);
        Assert.Equal("vocal_full", p.Id);
        Assert.Equal("Full", p.Label);
        Assert.Equal(PresetCategory.Vocals, p.Category);
        Assert.Equal("High quality vocal separation", p.Description);
        Assert.Equal(["model_a.onnx", "model_b.onnx"], p.AllModels);
        Assert.Equal("max_fft", p.EnsembleAlgorithm);
    }

    [Fact]
    public void ParsePresets_MultipleEntries_MapsAll()
    {
        var presets = PresetCatalogService.ParsePresets(
            """
            {"vocal_full":{"name":"Vocal Full","models":["v_model.onnx"]},
             "instrumental_basic":{"name":"Instrumental Basic","models":["i_model.onnx"]}}
            """
        );

        Assert.Equal(2, presets.Count);
        Assert.Contains(presets, p => p.Id == "vocal_full" && p.Category == PresetCategory.Vocals);
        Assert.Contains(
            presets,
            p => p.Id == "instrumental_basic" && p.Category == PresetCategory.Instrumentals
        );
    }

    [Fact]
    public void ParsePresets_EmptyNameField_FallsBackToId()
    {
        var presets = PresetCatalogService.ParsePresets(
            """{"some_preset":{"name":"","models":["m.onnx"]}}"""
        );

        Assert.Equal("some_preset", Assert.Single(presets).Label);
    }

    [Fact]
    public void ParsePresets_MissingNameField_FallsBackToId()
    {
        var presets = PresetCatalogService.ParsePresets(
            """{"some_preset":{"models":["m.onnx"]}}"""
        );

        Assert.Equal("some_preset", Assert.Single(presets).Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ParsePresets_NonObjectOrEmpty_ReturnsEmptyList(string raw) =>
        Assert.Empty(PresetCatalogService.ParsePresets(raw));

    [Fact]
    public void ParsePresets_CarriesAlgorithmIntoEnsembleAlgorithm()
    {
        var presets = PresetCatalogService.ParsePresets(
            """{"vocal_full":{"name":"Vocal Full","models":["model_a.onnx"],"algorithm":"max_fft"}}"""
        );

        Assert.Equal("max_fft", Assert.Single(presets).EnsembleAlgorithm);
    }

    [Fact]
    public void ParsePresets_MissingAlgorithm_LeavesEnsembleAlgorithmNull()
    {
        var presets = PresetCatalogService.ParsePresets(
            """{"vocal_full":{"name":"Vocal Full","models":["model_a.onnx"]}}"""
        );

        Assert.Null(Assert.Single(presets).EnsembleAlgorithm);
    }

    [Fact]
    public void ParsePresets_BlankAlgorithm_LeavesEnsembleAlgorithmNull()
    {
        var presets = PresetCatalogService.ParsePresets(
            """{"vocal_full":{"name":"Vocal Full","models":["model_a.onnx"],"algorithm":"   "}}"""
        );

        Assert.Null(Assert.Single(presets).EnsembleAlgorithm);
    }

    [Fact]
    public void ParsePresets_ToleratesSurroundingNoise()
    {
        var presets = PresetCatalogService.ParsePresets(
            """
            some stray stderr line
            {"vocal_full":{"name":"Vocal Full","models":["m.onnx"]}}
            trailing noise
            """
        );

        Assert.Equal("vocal_full", Assert.Single(presets).Id);
    }
}

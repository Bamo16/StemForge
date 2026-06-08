using System.Reflection;
using System.Text.Json;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Tests for the private static parsing helpers inside SeparatorDriverService.
/// Because these methods are private (not internal), they are invoked via reflection.
/// </summary>
public sealed class SeparatorDriverServiceTests
{
    private static readonly BindingFlags _privateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly Type _type = typeof(SeparatorDriverService);

    // ── InferPresetCategory ──────────────────────────────────────────────────

    [Theory]
    [InlineData("vocal_full", PresetCategory.Vocals)]
    [InlineData("vocal_some_preset", PresetCategory.Vocals)]
    [InlineData("instrumental_basic", PresetCategory.Instrumentals)]
    [InlineData("instrumental_low_res", PresetCategory.Instrumentals)]
    [InlineData("karaoke", PresetCategory.Instrumentals)]
    [InlineData("drums_only", PresetCategory.Other)]
    [InlineData("something_random", PresetCategory.Other)]
    public void InferPresetCategory_ReturnsExpectedCategory(string id, PresetCategory expected)
    {
        var method = _type.GetMethod("InferPresetCategory", _privateStatic)!;
        var result = (PresetCategory)method.Invoke(null, [id])!;
        Assert.Equal(expected, result);
    }

    // ── StripCategoryPrefix ──────────────────────────────────────────────────

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
    )
    {
        var method = _type.GetMethod("StripCategoryPrefix", _privateStatic)!;
        var result = (string)method.Invoke(null, [name, category])!;
        Assert.Equal(expected, result);
    }

    // ── ParsePresetsFromJson ─────────────────────────────────────────────────

    [Fact]
    public void ParsePresetsFromJson_ValidJson_PopulatesPresetFields()
    {
        const string json = """
            {
              "vocal_full": {
                "name": "Vocal Full",
                "description": "High quality vocal separation",
                "models": ["model_a.onnx", "model_b.onnx"]
              }
            }
            """;

        var presets = InvokeParse(json);

        Assert.Single(presets);
        var p = presets[0];
        Assert.Equal("vocal_full", p.Id);
        Assert.Equal("Full", p.Label);
        Assert.Equal(PresetCategory.Vocals, p.Category);
        Assert.Equal(["model_a.onnx", "model_b.onnx"], p.AllModels);
    }

    [Fact]
    public void ParsePresetsFromJson_MultipleEntries_MapsAllPresets()
    {
        const string json = """
            {
              "vocal_full": {
                "name": "Vocal Full",
                "description": "",
                "models": ["v_model.onnx"]
              },
              "instrumental_basic": {
                "name": "Instrumental Basic",
                "description": "",
                "models": ["i_model.onnx"]
              }
            }
            """;

        var presets = InvokeParse(json);

        Assert.Equal(2, presets.Count);
        Assert.Contains(presets, p => p.Id == "vocal_full" && p.Category == PresetCategory.Vocals);
        Assert.Contains(
            presets,
            p => p.Id == "instrumental_basic" && p.Category == PresetCategory.Instrumentals
        );
    }

    [Fact]
    public void ParsePresetsFromJson_EmptyNameField_FallsBackToId()
    {
        const string json = """
            {
              "some_preset": {
                "name": "",
                "description": "",
                "models": ["m.onnx"]
              }
            }
            """;

        var presets = InvokeParse(json);

        Assert.Single(presets);
        Assert.Equal("some_preset", presets[0].Label);
    }

    [Fact]
    public void ParsePresetsFromJson_EmptyObject_ReturnsEmptyList()
    {
        var presets = InvokeParse("{}");
        Assert.Empty(presets);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<Preset> InvokeParse(string json)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        var method = _type.GetMethod("ParsePresetsFromJson", _privateStatic)!;
        return (IReadOnlyList<Preset>)method.Invoke(null, [element])!;
    }
}

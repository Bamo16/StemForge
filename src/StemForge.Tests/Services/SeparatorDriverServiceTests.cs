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

    [Fact]
    public void ParsePresetsFromJson_CarriesAlgorithmIntoEnsembleAlgorithm()
    {
        const string json = """
            {
              "vocal_full": {
                "name": "Vocal Full",
                "description": "High quality vocal separation",
                "models": ["model_a.onnx", "model_b.onnx"],
                "algorithm": "max_fft"
              }
            }
            """;

        var presets = InvokeParse(json);

        Assert.Single(presets);
        Assert.Equal("max_fft", presets[0].EnsembleAlgorithm);
    }

    [Fact]
    public void ParsePresetsFromJson_MissingAlgorithm_LeavesEnsembleAlgorithmNull()
    {
        const string json = """
            {
              "vocal_full": {
                "name": "Vocal Full",
                "description": "",
                "models": ["model_a.onnx", "model_b.onnx"]
              }
            }
            """;

        var presets = InvokeParse(json);

        Assert.Single(presets);
        Assert.Null(presets[0].EnsembleAlgorithm);
    }

    [Fact]
    public void ParsePresetsFromJson_BlankAlgorithm_LeavesEnsembleAlgorithmNull()
    {
        const string json = """
            {
              "vocal_full": {
                "name": "Vocal Full",
                "description": "",
                "models": ["model_a.onnx", "model_b.onnx"],
                "algorithm": "   "
              }
            }
            """;

        var presets = InvokeParse(json);

        Assert.Single(presets);
        Assert.Null(presets[0].EnsembleAlgorithm);
    }

    // ── BuildTerminationMessage ──────────────────────────────────────────────

    [Fact]
    public void BuildTerminationMessage_IncludesExitCodeAndStderrTail()
    {
        var message = InvokeBuildTerminationMessage(
            exitCode: 137,
            stderrTail: ["loading model", "CUDA out of memory", "torch.OutOfMemoryError"]
        );

        Assert.Contains("137", message);
        Assert.Contains("CUDA out of memory", message);
        Assert.Contains("torch.OutOfMemoryError", message);
    }

    [Fact]
    public void BuildTerminationMessage_UnknownExitCode_StillReportsTail()
    {
        var message = InvokeBuildTerminationMessage(exitCode: null, stderrTail: ["fatal error"]);

        Assert.Contains("unknown", message);
        Assert.Contains("fatal error", message);
    }

    [Fact]
    public void BuildTerminationMessage_NoStderr_ReportsCodeWithoutTail()
    {
        var message = InvokeBuildTerminationMessage(exitCode: 1, stderrTail: []);

        Assert.Contains("1", message);
        Assert.Contains("no output", message);
    }

    // ── BoundedStderrBuffer ──────────────────────────────────────────────────

    [Fact]
    public void BoundedStderrBuffer_KeepsOnlyMostRecentLinesUpToCapacity()
    {
        var buffer = NewBoundedBuffer(capacity: 3);

        for (var i = 1; i <= 100; i++)
            BufferAdd(buffer, $"line {i}");

        var snapshot = BufferSnapshot(buffer);

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["line 98", "line 99", "line 100"], snapshot);
    }

    [Fact]
    public void BoundedStderrBuffer_BelowCapacity_PreservesOrder()
    {
        var buffer = NewBoundedBuffer(capacity: 30);

        BufferAdd(buffer, "first");
        BufferAdd(buffer, "second");

        Assert.Equal(["first", "second"], BufferSnapshot(buffer));
    }

    [Fact]
    public void BoundedStderrBuffer_Clear_EmptiesBuffer()
    {
        var buffer = NewBoundedBuffer(capacity: 5);
        BufferAdd(buffer, "stale");

        BufferClear(buffer);

        Assert.Empty(BufferSnapshot(buffer));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string InvokeBuildTerminationMessage(
        int? exitCode,
        IReadOnlyList<string> stderrTail
    )
    {
        var method = _type.GetMethod("BuildTerminationMessage", _privateStatic)!;
        return (string)method.Invoke(null, [exitCode, stderrTail])!;
    }

    private static readonly Type _bufferType = _type.GetNestedType(
        "BoundedStderrBuffer",
        BindingFlags.NonPublic
    )!;

    private static object NewBoundedBuffer(int capacity) =>
        Activator.CreateInstance(_bufferType, [capacity])!;

    private static void BufferAdd(object buffer, string line) =>
        _bufferType.GetMethod("Add")!.Invoke(buffer, [line]);

    private static void BufferClear(object buffer) =>
        _bufferType.GetMethod("Clear")!.Invoke(buffer, []);

    private static IReadOnlyList<string> BufferSnapshot(object buffer) =>
        (IReadOnlyList<string>)_bufferType.GetMethod("Snapshot")!.Invoke(buffer, [])!;

    private static IReadOnlyList<Preset> InvokeParse(string json)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        var method = _type.GetMethod("ParsePresetsFromJson", _privateStatic)!;
        return (IReadOnlyList<Preset>)method.Invoke(null, [element])!;
    }
}

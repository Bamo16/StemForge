using StemForge.Models;
using StemForge.Services;

namespace StemForge.Tests.Services;

public sealed class SeparationServiceTests
{
    // ── ParseStepLabel ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Starting separation of audio track", "Separating")]
    [InlineData("INFO:audio_separator.separator:Starting separation of file.flac", "Separating")]
    [InlineData("Processing with model: Kim_Vocal_2.onnx", "Loading model")]
    [InlineData("Loading model from cache", "Loading model")]
    [InlineData("Running ensemble processing for vocals", "Creating ensemble")]
    [InlineData("Creating ensemble output", "Creating ensemble")]
    [InlineData("Downloading model weights", "Downloading model")]
    [InlineData("Some other info line", null)]
    [InlineData("", null)]
    public void ParseStepLabel_KnownPatterns(string line, string? expected)
    {
        Assert.Equal(expected, SeparationService.ParseStepLabel(line));
    }

    // ── CleanLogLine ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(
        "2024-01-01 12:00:00 - INFO - audio_separator - Starting separation",
        "audio_separator - Starting separation"
    )]
    [InlineData(
        "2024-01-01 12:00:00 - WARNING - audio_separator - Model not found on disk",
        "audio_separator - Model not found on disk"
    )]
    [InlineData("INFO:audio_separator.separator:Processing track", "Processing track")]
    [InlineData("Plain message without prefix", "Plain message without prefix")]
    public void CleanLogLine_StripsPrefix(string raw, string? expected)
    {
        Assert.Equal(expected, SeparationService.CleanLogLine(raw));
    }

    // ── BuildArgs ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildArgs_BuiltinPreset_UsesEnsemblePreset()
    {
        var preset = new Preset(
            Id: "karaoke",
            Label: "Karaoke",
            Category: PresetCategory.Vocals,
            Description: "desc",
            ModelCount: 1,
            Vram: "4GB",
            Mode: SeparationMode.BuiltinPreset,
            PrimaryModel: "karaoke"
        );

        var args = SeparationService.BuildArgs("input.flac", preset, "/models", "/out").ToList();

        Assert.Contains("--ensemble_preset", args);
        Assert.Contains("karaoke", args);
        Assert.Contains("--model_file_dir", args);
        Assert.Contains("/models", args);
        Assert.Contains("--output_dir", args);
        Assert.Contains("/out", args);
        Assert.Contains("--output_format", args);
        Assert.Contains("FLAC", args);
    }

    [Fact]
    public void BuildArgs_SingleModel_UsesModelFilename()
    {
        var preset = new Preset(
            Id: "custom",
            Label: "Custom",
            Category: PresetCategory.Other,
            Description: "desc",
            ModelCount: 1,
            Vram: "4GB",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "Kim_Vocal_2.onnx"
        );

        var args = SeparationService.BuildArgs("input.flac", preset, "/models", "/out").ToList();

        Assert.Contains("--model_filename", args);
        Assert.Contains("Kim_Vocal_2.onnx", args);
        Assert.DoesNotContain("--ensemble_preset", args);
    }

    [Fact]
    public void BuildArgs_CustomEnsemble_IncludesExtraModels()
    {
        var preset = new Preset(
            Id: "ens",
            Label: "Ensemble",
            Category: PresetCategory.Vocals,
            Description: "desc",
            ModelCount: 2,
            Vram: "8GB",
            Mode: SeparationMode.CustomEnsemble,
            PrimaryModel: "ModelA.onnx",
            EnsembleAlgorithm: "avg_wave",
            ExtraModels: ["ModelB.onnx", "ModelC.onnx"]
        );

        var args = SeparationService.BuildArgs("input.flac", preset, "/models", "/out").ToList();

        Assert.Contains("--extra_models", args);
        Assert.Contains("ModelB.onnx", args);
        Assert.Contains("ModelC.onnx", args);
        Assert.Contains("--ensemble_algorithm", args);
        Assert.Contains("avg_wave", args);
    }

    // ── FindStem ──────────────────────────────────────────────────────────────

    [Fact]
    public void FindStem_MatchingFile_ReturnsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemtest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "track (Vocals).flac");
        File.WriteAllText(file, "");

        try
        {
            var found = SeparationService.FindStem(dir, PresetCategory.Vocals);
            Assert.Equal(file, found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindStem_NoMatchingFile_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemtest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "track (Instrumental).flac"), "");

        try
        {
            Assert.Null(SeparationService.FindStem(dir, PresetCategory.Vocals));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

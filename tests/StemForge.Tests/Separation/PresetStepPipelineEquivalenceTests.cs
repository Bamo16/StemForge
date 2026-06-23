namespace StemForge.Tests.Separation;

/// <summary>
/// Confirms the ordered-steps preset model is behaviourally transparent to the pipeline: a
/// single-step preset that has been persisted as a steps list and reloaded drives
/// <see cref="SeparationPipeline.BuildRequest"/> to exactly the same <see cref="JobRequest"/> as the
/// equivalent freshly-built flat preset. This is the "no behavior change for single-step presets"
/// acceptance criterion expressed at the request-building seam.
/// </summary>
public sealed class PresetStepPipelineEquivalenceTests
{
    private const string AudioPath = "/tmp/song.flac";
    private const string OutputDir = "/tmp/out";
    private const string Format = "FLAC";

    [Fact]
    public void BuildRequest_SingleModel_ReloadedAsSteps_MatchesFlatPreset()
    {
        var flat = new Preset(
            Id: "single",
            Label: "Single",
            Category: PresetCategory.Other,
            Description: "one model",
            ModelCount: 1,
            Vram: "",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "only.onnx"
        );

        AssertEquivalentAfterRoundTrip(flat);
    }

    [Fact]
    public void BuildRequest_CustomEnsemble_ReloadedAsSteps_MatchesFlatPreset()
    {
        var flat = new Preset(
            Id: "ensemble",
            Label: "Ensemble",
            Category: PresetCategory.Other,
            Description: "three models",
            ModelCount: 3,
            Vram: "",
            Mode: SeparationMode.CustomEnsemble,
            PrimaryModel: "p.ckpt",
            EnsembleAlgorithm: "avg_fft",
            ExtraModels: ["x.ckpt", "y.ckpt"]
        );

        AssertEquivalentAfterRoundTrip(flat);
    }

    private static void AssertEquivalentAfterRoundTrip(Preset flat)
    {
        var reloaded = SaveAndReload(flat);

        var fromFlat = SeparationPipeline.BuildRequest(flat, AudioPath, OutputDir, Format);
        var fromSteps = SeparationPipeline.BuildRequest(reloaded, AudioPath, OutputDir, Format);

        // JobRequest is a record, but Models is a list (reference equality under record ==), so
        // compare the request fields the driver actually consumes element-by-element.
        Assert.Equal(fromFlat.PresetId, fromSteps.PresetId);
        Assert.Equal(fromFlat.Algorithm, fromSteps.Algorithm);
        Assert.Equal(fromFlat.Models, fromSteps.Models);
        Assert.Equal(fromFlat.OutputFormat, fromSteps.OutputFormat);
    }

    private static Preset SaveAndReload(Preset preset)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemforge-eq-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var roaming = Path.Combine(dir, "user_presets.json");
        var legacy = Path.Combine(dir, "legacy.json");
        try
        {
            new UserPresetService(roaming).Add(preset);
            var svc = UserPresetService.Load(roaming, legacy);
            return Assert.Single(svc.Presets);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

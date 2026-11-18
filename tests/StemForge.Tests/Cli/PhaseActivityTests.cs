using StemForge.Cli.Progress;

namespace StemForge.Tests.Cli;

public sealed class PhaseActivityTests
{
    [Theory]
    [InlineData("downloading", null, "Downloading source")]
    [InlineData("starting", null, "Starting")]
    [InlineData("starting", "", "Starting")]
    [InlineData("starting", "Vocals", "Starting Vocals")]
    [InlineData("stem_written", null, "Writing stems")]
    [InlineData("tagging", null, "Tagging output")]
    [InlineData("keep_source", null, "Keeping source")]
    [InlineData("run_complete", null, "Finished")]
    [InlineData("run_complete", "Drums", "Finished Drums")]
    [InlineData("log", null, null)]
    [InlineData("done", null, null)]
    [InlineData("", null, null)]
    public void Describe_PhaseWithOptionalRunLabel_NamesTheActivity(
        string phase,
        string? runLabel,
        string? expected
    ) =>
        Assert.Equal(
            expected,
            PhaseActivity.Describe(new JobUpdate { Phase = phase, RunLabel = runLabel })
        );

    [Theory]
    [InlineData(null, null, null, false, "Loading model")]
    [InlineData(null, null, null, true, "Loading model (cached)")]
    [InlineData("a.ckpt", 1, 1, false, "Loading model")]
    [InlineData(null, 2, 3, false, "Loading model 2/3")]
    [InlineData("a.ckpt", 2, 3, false, "Loading model 2/3: a.ckpt")]
    [InlineData("a.ckpt", 2, 3, true, "Loading model 2/3: a.ckpt (cached)")]
    [InlineData("", 2, 3, false, "Loading model 2/3")]
    public void Describe_LoadingModel_ShowsIndexOfCountOnlyForMultiModelRuns(
        string? model,
        int? index,
        int? count,
        bool cached,
        string expected
    ) =>
        Assert.Equal(
            expected,
            PhaseActivity.Describe(
                new JobUpdate
                {
                    Phase = "loading_model",
                    Model = model,
                    ModelIndex = index,
                    ModelCount = count,
                    Cached = cached ? true : null,
                }
            )
        );

    [Fact]
    public void Describe_DownloadingModel_NamesTheModelWhenKnown()
    {
        Assert.Equal(
            "Fetching model a.ckpt",
            PhaseActivity.Describe(new JobUpdate { Phase = "downloading_model", Model = "a.ckpt" })
        );
        Assert.Equal(
            "Fetching model",
            PhaseActivity.Describe(new JobUpdate { Phase = "downloading_model" })
        );
    }

    [Theory]
    [InlineData(null, "Combining stems")]
    [InlineData("", "Combining stems")]
    [InlineData("Vocals", "Combining Vocals")]
    public void Describe_Ensembling_NamesTheStemBeingCombined(string? stem, string expected) =>
        Assert.Equal(
            expected,
            PhaseActivity.Describe(new JobUpdate { Phase = "ensembling", Stem = stem })
        );

    [Fact]
    public void Describe_ProgressTickWithoutModelDetail_UsesTheCallersRememberedModel()
    {
        // The driver names the model on loading_model only; later ticks rely on the caller's memory.
        var tick = new JobUpdate { Phase = "progress", RunLabel = "Vocals" };

        var text = PhaseActivity.Describe(tick, model: "b.ckpt", modelIndex: 2, modelCount: 3);

        Assert.Equal("Separating Vocals (model 2/3: b.ckpt)", text);
    }

    [Fact]
    public void Describe_UpdateCarriesItsOwnModelDetail_ItWinsOverTheCallersMemory()
    {
        var tick = new JobUpdate
        {
            Phase = "progress",
            Model = "new.ckpt",
            ModelIndex = 1,
            ModelCount = 2,
        };

        var text = PhaseActivity.Describe(tick, model: "old.ckpt", modelIndex: 2, modelCount: 3);

        Assert.Equal("Separating (model 1/2: new.ckpt)", text);
    }

    [Fact]
    public void Describe_ProgressOnASingleModelRun_OmitsTheModelSuffix() =>
        Assert.Equal(
            "Separating",
            PhaseActivity.Describe(
                new JobUpdate { Phase = "progress" },
                model: "a.ckpt",
                modelIndex: 1,
                modelCount: 1
            )
        );
}

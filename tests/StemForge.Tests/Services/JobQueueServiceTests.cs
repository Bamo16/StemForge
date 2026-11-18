using StemForge.Services;
using StemForge.ViewModels;

namespace StemForge.Tests.Services;

public sealed class JobQueueServiceTests
{
    private readonly JobItemViewModel _job = new(
        new JobRecord(Guid.NewGuid(), @"C:\music\song.flac", null, [], "out", "models")
    );
    private readonly JobQueueService.RunProgressState _state = new();

    private string[] Log => _job.LogOutput.Length is 0 ? [] : _job.LogOutput.Split('\n');

    private void Apply(JobUpdate update) => JobQueueService.ApplyUpdate(_job, update, _state);

    [Fact]
    public void Downloading_ResetsProgressAndSaysSo()
    {
        _job.Progress = 40;

        Apply(new JobUpdate { Phase = "downloading" });

        Assert.Equal(0, _job.Progress);
        Assert.Equal("Downloading…", _job.StatusText);
    }

    [Fact]
    public void Resolved_RenamesTheJobToTheSourceName()
    {
        Apply(new JobUpdate { Phase = "resolved", SourceName = "Artist - Title" });

        Assert.Equal("Artist - Title", _job.InputFileName);
    }

    [Fact]
    public void Resolved_WithNoName_LeavesTheNameAlone()
    {
        Apply(new JobUpdate { Phase = "resolved", SourceName = "" });

        Assert.Equal("song.flac", _job.InputFileName);
    }

    [Theory]
    [InlineData(0, 1, "")]
    [InlineData(0, 3, "1/3")]
    [InlineData(2, 3, "3/3")]
    public void Starting_ShowsTheRunCounterOnlyWhenThereAreSeveralRuns(
        int runIndex,
        int runCount,
        string counter
    )
    {
        Apply(
            new JobUpdate
            {
                Phase = "starting",
                RunIndex = runIndex,
                RunCount = runCount,
                RunLabel = "Vocals",
            }
        );

        Assert.Equal(counter, _job.PresetCounter);
        Assert.Equal("Vocals — Starting…", _job.StatusText);
    }

    [Fact]
    public void DownloadingModel_CachedModel_SaysNothing()
    {
        Apply(
            new JobUpdate
            {
                Phase = "downloading_model",
                Cached = true,
                RunLabel = "Vocals",
            }
        );

        Assert.Equal(string.Empty, _job.StatusText);
        Assert.Empty(Log);
    }

    [Fact]
    public void DownloadingModel_UncachedMultiModelRun_NumbersTheModelInStatusAndLog()
    {
        Apply(
            new JobUpdate
            {
                Phase = "downloading_model",
                Cached = false,
                RunLabel = "Vocals",
                RunCount = 2,
                ModelIndex = 2,
                ModelCount = 3,
            }
        );

        Assert.Equal("Vocals — Downloading model 2/3…", _job.StatusText);
        Assert.Equal(["[Vocals] Downloading model 2/3…"], Log);
    }

    [Fact]
    public void DownloadingModel_UncachedSingleModelRun_UsesThePlainLabelAsLogPrefix()
    {
        Apply(
            new JobUpdate
            {
                Phase = "downloading_model",
                Cached = false,
                RunLabel = "Vocals",
                RunCount = 1,
                ModelCount = 1,
            }
        );

        Assert.Equal("Vocals — Downloading model…", _job.StatusText);
        Assert.Equal(["Vocals Downloading model…"], Log);
    }

    [Fact]
    public void LoadingModel_NamedModelOfSeveral_LogsItsNameWithoutExtensionAndQueuesTheRunLine()
    {
        Apply(
            new JobUpdate
            {
                Phase = "loading_model",
                RunLabel = "Vocals",
                RunCount = 1,
                Model = "bs_roformer_x.ckpt",
                ModelIndex = 2,
                ModelCount = 3,
            }
        );

        Assert.Equal("Vocals — Loading model 2/3…", _job.StatusText);
        Assert.Equal(["Vocals Loading bs_roformer_x (model 2/3)…"], Log);
        Assert.Equal(2, _state.ModelIndex);
        Assert.Equal(3, _state.ModelCount);
        Assert.Equal("Vocals Running…", _state.PendingRunLogLine);
    }

    [Theory]
    [InlineData("m.ckpt", 1, "Vocals Loading m…")]
    [InlineData(null, 1, "Vocals Loading model…")]
    [InlineData(null, 2, "Vocals Loading model 1/2…")]
    public void LoadingModel_LogLine_DependsOnWhetherTheModelIsNamedAndHowManyThereAre(
        string? model,
        int modelCount,
        string expected
    )
    {
        Apply(
            new JobUpdate
            {
                Phase = "loading_model",
                RunLabel = "Vocals",
                Model = model,
                ModelIndex = 1,
                ModelCount = modelCount,
            }
        );

        Assert.Equal([expected], Log);
    }

    [Fact]
    public void LoadingModel_NoIndexOrCount_DefaultsTheRunStateToOneOfOne()
    {
        _state.ModelIndex = 5;
        _state.ModelCount = 5;

        Apply(new JobUpdate { Phase = "loading_model", RunLabel = "Vocals" });

        Assert.Equal(1, _state.ModelIndex);
        Assert.Equal(1, _state.ModelCount);
    }

    [Theory]
    [InlineData(null, "Vocals — Separating…", "Vocals Separating…")]
    [InlineData(1, "Vocals — Separating…", "Vocals Separating…")]
    [InlineData(3, "Vocals — Separating (3 models)…", "Vocals Separating (3 models)…")]
    public void Separating_MentionsTheModelCountOnlyForEnsembles(
        int? modelCount,
        string status,
        string log
    )
    {
        Apply(
            new JobUpdate
            {
                Phase = "separating",
                RunLabel = "Vocals",
                ModelCount = modelCount,
            }
        );

        Assert.Equal(status, _job.StatusText);
        Assert.Equal([log], Log);
    }

    [Fact]
    public void Progress_FirstTickAfterLoading_LogsTheRunLineOnceAndAdvancesTheBar()
    {
        Apply(
            new JobUpdate
            {
                Phase = "loading_model",
                RunLabel = "Vocals",
                ModelIndex = 1,
                ModelCount = 2,
            }
        );
        var tick = new JobUpdate
        {
            Phase = "progress",
            RunLabel = "Vocals",
            ProgressCurrent = 1,
            ProgressTotal = 10,
            OverallPercent = 12,
        };

        Apply(tick);
        Apply(tick with { OverallPercent = 20 });

        Assert.Equal(1, Log.Count(line => line == "Vocals Running…"));
        Assert.Null(_state.PendingRunLogLine);
        Assert.Equal(20, _job.Progress);
        Assert.Equal("Vocals — Running model 1/2…", _job.StatusText);
    }

    [Fact]
    public void Progress_SinglemodelRun_StatusHasNoModelNumbers()
    {
        Apply(
            new JobUpdate
            {
                Phase = "progress",
                RunLabel = "Vocals",
                ProgressCurrent = 1,
                ProgressTotal = 4,
                OverallPercent = 5,
            }
        );

        Assert.Equal("Vocals — Running…", _job.StatusText);
    }

    [Fact]
    public void Progress_PercentFallingBack_NeverMovesTheBarBackwards()
    {
        _job.Progress = 60;

        Apply(
            new JobUpdate
            {
                Phase = "progress",
                ProgressCurrent = 1,
                ProgressTotal = 4,
                OverallPercent = 30,
            }
        );

        Assert.Equal(60, _job.Progress);
    }

    [Fact]
    public void Progress_TickWithoutATotal_ChangesNeitherBarNorStatus()
    {
        _job.StatusText = "unchanged";

        Apply(
            new JobUpdate
            {
                Phase = "progress",
                ProgressCurrent = 3,
                OverallPercent = 50,
            }
        );

        Assert.Equal(0, _job.Progress);
        Assert.Equal("unchanged", _job.StatusText);
    }

    [Fact]
    public void Progress_WhileCombining_KeepsTheCombiningStatus()
    {
        Apply(
            new JobUpdate
            {
                Phase = "ensembling",
                RunLabel = "Vocals",
                Stem = "vocals",
            }
        );

        Apply(
            new JobUpdate
            {
                Phase = "progress",
                RunLabel = "Vocals",
                ProgressCurrent = 1,
                ProgressTotal = 4,
                OverallPercent = 80,
            }
        );

        Assert.Equal("Vocals — Combining vocals…", _job.StatusText);
        Assert.Equal(80, _job.Progress);
    }

    [Theory]
    [InlineData("vocals", "Vocals — Combining vocals…", "Vocals Combining vocals…")]
    [InlineData(null, "Vocals — Combining stems…", "Vocals Combining stems…")]
    public void Ensembling_NamesTheStemWhenKnown(string? stem, string status, string log)
    {
        Apply(
            new JobUpdate
            {
                Phase = "ensembling",
                RunLabel = "Vocals",
                Stem = stem,
            }
        );

        Assert.Equal(status, _job.StatusText);
        Assert.Equal([log], Log);
    }

    [Fact]
    public void StemWritten_LogsTheStemNameNotTheSeparatorsTemporaryPath()
    {
        Apply(
            new JobUpdate
            {
                Phase = "stem_written",
                RunLabel = "Vocals",
                RunCount = 2,
                Stem = "vocals",
                OutputPath = @"C:\tmp\raw_output.flac",
            }
        );

        Assert.Equal(["[Vocals] Wrote vocals"], Log);
    }

    [Fact]
    public void StemWritten_WithoutAStemName_LogsNothing()
    {
        Apply(new JobUpdate { Phase = "stem_written", OutputPath = @"C:\tmp\raw.flac" });

        Assert.Empty(Log);
    }

    [Theory]
    [InlineData("warning", true)]
    [InlineData("error", true)]
    [InlineData("info", false)]
    [InlineData(null, false)]
    public void Log_OnlyWarningsAndErrorsReachTheTimeline(string? level, bool shown)
    {
        Apply(
            new JobUpdate
            {
                Phase = "log",
                LogLevel = level,
                LogMessage = "something happened",
            }
        );

        Assert.Equal(shown, Log.Contains("something happened"));
    }

    [Fact]
    public void Log_WarningLevelMessageShownOnTheTimelineBeforeAnyFlush()
    {
        Apply(
            new JobUpdate
            {
                Phase = "log",
                LogLevel = "warning",
                LogMessage = "careful",
            }
        );

        Assert.Equal(["careful"], Log);
    }

    [Fact]
    public void Log_InfoMessage_StaysOffTheTimeline()
    {
        Apply(
            new JobUpdate
            {
                Phase = "log",
                LogLevel = "info",
                LogMessage = "chatter",
            }
        );

        Assert.Empty(Log);
    }

    [Fact]
    public void Log_BitsPerSampleWarning_IsKeptRawButNotShown()
    {
        Apply(
            new JobUpdate
            {
                Phase = "log",
                LogLevel = "warning",
                LogMessage = "output bits-per-sample changed",
            }
        );

        Assert.Empty(Log);
        _job.FlushRawLogToOutput();
        Assert.Equal("output bits-per-sample changed", _job.LogOutput);
    }

    [Fact]
    public void RunComplete_LogsEachFinalPathSetsTheBarAndResetsTheRunState()
    {
        _state.ModelIndex = 2;
        _state.ModelCount = 3;
        _state.PendingRunLogLine = "stale";

        Apply(
            new JobUpdate
            {
                Phase = "run_complete",
                OverallPercent = 50,
                WrittenPaths = [@"C:\out\a (vocals).flac", @"C:\out\a (drums).flac"],
            }
        );

        Assert.Equal([@"C:\out\a (vocals).flac", @"C:\out\a (drums).flac"], Log);
        Assert.Equal(50, _job.Progress);
        Assert.Equal(1, _state.ModelIndex);
        Assert.Equal(1, _state.ModelCount);
        Assert.Null(_state.PendingRunLogLine);
    }

    [Fact]
    public void KeepSource_SaysItIsSavingTheSource()
    {
        Apply(new JobUpdate { Phase = "keep_source" });

        Assert.Equal("Saving source…", _job.StatusText);
    }

    [Fact]
    public void UnknownPhase_ChangesNothing()
    {
        _job.StatusText = "before";

        Apply(new JobUpdate { Phase = "tagging", RunLabel = "Vocals" });

        Assert.Equal("before", _job.StatusText);
        Assert.Empty(Log);
        Assert.Equal(0, _job.Progress);
    }
}

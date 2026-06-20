using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.Services;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

/// <summary>
/// Tests for the job feed timeline behaviour: meaningful phase transitions produce
/// log entries, raw INFO lines are suppressed, and full raw output is flushed on failure.
/// </summary>
public sealed class JobFeedTimelineTests
{
    private static JobItemViewModel MakeVm() =>
        new(
            new JobRecord(
                Guid.NewGuid(),
                InputFilePath: "/tmp/track.flac",
                SourceUrl: null,
                Presets: [],
                OutputDir: "/tmp/out",
                ModelsDir: "/tmp/models"
            )
        );

    /// <summary>
    /// Converts a <see cref="JobProgress"/> into a <see cref="JobUpdate"/> and invokes
    /// <see cref="JobQueueService.ApplyUpdate"/> — mirrors the translation the pipeline
    /// adapter does at runtime.
    /// </summary>
    private static void Invoke(
        JobItemViewModel vm,
        JobProgress p,
        int presetIndex = 0,
        int totalSteps = 1,
        string presetLabel = "Vocals Clean",
        JobQueueService.RunProgressState? state = null
    )
    {
        state ??= new JobQueueService.RunProgressState();

        var phase = p as PhaseProgress;
        var tick = p as ProgressTick;
        var log = p as LogLine;
        var stem = p as StemWritten;

        // Mirror the model-tracking update that SeparationPipeline does before reporting.
        if (phase is { Phase: JobPhase.LoadingModel })
        {
            state.ModelIndex = phase.ModelIndex ?? 1;
            state.ModelCount = phase.ModelCount ?? 1;
        }

        int overallPercent;
        if (tick is { Total: > 0 and var total, Current: { } cur })
        {
            var withinModel = Math.Min(100, cur * 100 / total);
            var withinPreset = ((state.ModelIndex - 1) * 100 + withinModel) / state.ModelCount;
            overallPercent = (int)Math.Round((presetIndex * 100.0 + withinPreset) / totalSteps);
        }
        else
        {
            overallPercent = (int)Math.Round(presetIndex * 100.0 / totalSteps);
        }

        var update = new JobUpdate
        {
            Phase = p.UpdatePhase,
            RunIndex = presetIndex,
            RunCount = totalSteps,
            RunLabel = presetLabel,
            Model = phase?.Model,
            ModelIndex = phase?.ModelIndex,
            ModelCount = phase?.ModelCount,
            Cached = phase?.Cached,
            Stem = phase?.Stem,
            ProgressCurrent = tick?.Current,
            ProgressTotal = tick?.Total,
            ProgressFinal = tick?.Final,
            OutputPath = stem?.Path,
            LogMessage = log?.Message,
            LogLevel = log?.Level,
            OverallPercent = overallPercent,
        };

        JobQueueService.ApplyUpdate(vm, update, state);
    }

    // ── loading_model emits a feed entry ────────────────────────────────────

    [Fact]
    public void LoadingModel_SingleModel_AppendsLoadingLineToFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 1 });

        Assert.Contains("Loading model", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_MultiModel_IncludesIndexInFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 2 });

        Assert.Contains("Loading model 1/2", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_MultiPreset_IncludesPresetLabelInFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 1 },
            presetIndex: 0,
            totalSteps: 2,
            presetLabel: "Vocal Clean"
        );

        Assert.Contains("Vocal Clean", vm.LogOutput);
    }

    // ── downloading_model emits a feed entry only when not cached ───────────

    [Fact]
    public void DownloadingModel_NotCached_AppendsDownloadLineToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.DownloadingModel)
            {
                ModelIndex = 1,
                ModelCount = 1,
                Cached = false,
            }
        );

        Assert.Contains("Downloading model", vm.LogOutput);
    }

    [Fact]
    public void DownloadingModel_Cached_DoesNotAppendToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.DownloadingModel)
            {
                ModelIndex = 1,
                ModelCount = 1,
                Cached = true,
            }
        );

        Assert.True(string.IsNullOrEmpty(vm.LogOutput));
    }

    // ── separating emits a feed entry ───────────────────────────────────────

    [Fact]
    public void Separating_AppendsSeperatingLineToFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.Separating) { ModelCount = 1 });

        Assert.Contains("Separating", vm.LogOutput);
    }

    [Fact]
    public void Separating_MultiModel_IncludesCountInFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.Separating) { ModelCount = 3 });

        Assert.Contains("3 models", vm.LogOutput);
    }

    // ── ensembling emits a feed entry ────────────────────────────────────────

    [Fact]
    public void Ensembling_WithStem_AppendsCombiningLineWithStem()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.Ensembling) { Stem = "Vocals" });

        Assert.Contains("Combining Vocals", vm.LogOutput);
    }

    [Fact]
    public void Ensembling_WithoutStem_AppendsCombiningStemsLine()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.Ensembling));

        Assert.Contains("Combining stems", vm.LogOutput);
    }

    // ── log phase filtering ──────────────────────────────────────────────────

    [Fact]
    public void Log_InfoLevel_DoesNotAppendToFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new LogLine { Level = "info", Message = "Input audio subtype: PCM_24" });

        Assert.True(string.IsNullOrEmpty(vm.LogOutput));
    }

    [Fact]
    public void Log_WarningLevel_AppendsToFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new LogLine { Level = "warning", Message = "Something looks wrong" });

        Assert.Contains("Something looks wrong", vm.LogOutput);
    }

    [Fact]
    public void Log_ErrorLevel_AppendsToFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new LogLine { Level = "error", Message = "Fatal error in model" });

        Assert.Contains("Fatal error in model", vm.LogOutput);
    }

    // ── raw log flush on failure ─────────────────────────────────────────────

    [Fact]
    public void FlushRawLogToOutput_ReplacesLogOutputWithAccumulatedRawLines()
    {
        var vm = MakeVm();

        // Accumulate raw log lines (as if the "log"/"info" path ran).
        vm.AccumulateRawLog("INFO line 1");
        vm.AccumulateRawLog("INFO line 2");

        // Append a timeline entry (e.g. from loading_model).
        vm.AppendLog("Loading model…");

        // Simulates the failure path.
        vm.FlushRawLogToOutput();

        Assert.Contains("INFO line 1", vm.LogOutput);
        Assert.Contains("INFO line 2", vm.LogOutput);
    }

    [Fact]
    public void FlushRawLogToOutput_WhenNoRawLines_LogOutputUnchanged()
    {
        var vm = MakeVm();
        vm.AppendLog("Loading model…");

        vm.FlushRawLogToOutput();

        // No raw lines accumulated — LogOutput should still show the timeline.
        Assert.Contains("Loading model", vm.LogOutput);
    }

    // ── progress ticks do not produce feed entries (absent pending run line) ──

    [Fact]
    public void Progress_Tick_DoesNotAppendToFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new ProgressTick { Current = 50, Total = 100 });

        Assert.True(string.IsNullOrEmpty(vm.LogOutput));
    }

    // ── "Running model" deferred log line ────────────────────────────────────

    [Fact]
    public void LoadingModel_WithModelName_ShowsNameAndIndex()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel)
            {
                Model = "bs_roformer_vocals.ckpt",
                ModelIndex = 1,
                ModelCount = 2,
            }
        );

        Assert.Contains("Loading bs_roformer_vocals (model 1/2)", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_SingleModelWithName_ShowsNameOnly()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel)
            {
                Model = "bs_roformer_vocals.ckpt",
                ModelIndex = 1,
                ModelCount = 1,
            }
        );

        Assert.Contains("Loading bs_roformer_vocals", vm.LogOutput);
        Assert.DoesNotContain("model 1/1", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_NoModelName_FallsBackToIndex()
    {
        var vm = MakeVm();

        Invoke(vm, new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 2, ModelCount = 3 });

        Assert.Contains("Loading model 2/3", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_ArmsRunLine_ButDoesNotEmitImmediately()
    {
        var vm = MakeVm();
        var state = new JobQueueService.RunProgressState();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 1 },
            state: state
        );

        // Loading entry is visible immediately…
        Assert.Contains("Loading", vm.LogOutput);
        // …but the run line is only armed, not emitted yet.
        Assert.DoesNotContain("Running", vm.LogOutput);
        Assert.NotNull(state.PendingRunLogLine);
    }

    [Fact]
    public void Progress_AfterLoadingModel_EmitsRunLine()
    {
        var vm = MakeVm();
        var state = new JobQueueService.RunProgressState();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 1 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 10, Total = 100 }, state: state);

        Assert.Contains("Running", vm.LogOutput);
        Assert.Null(state.PendingRunLogLine);
    }

    [Fact]
    public void Progress_SecondTick_DoesNotEmitRunLineAgain()
    {
        var vm = MakeVm();
        var state = new JobQueueService.RunProgressState();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 1 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 10, Total = 100 }, state: state);
        Invoke(vm, new ProgressTick { Current = 20, Total = 100 }, state: state);

        // Count occurrences of "Running" — should appear exactly once.
        var count = 0;
        var idx = 0;
        while ((idx = vm.LogOutput.IndexOf("Running", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx++;
        }
        Assert.Equal(1, count);
    }

    // ── progress bar model-aware calculation ─────────────────────────────────

    [Fact]
    public void Progress_MultiModelPreset_Model1DoesNotReach100()
    {
        var vm = MakeVm();
        var state = new JobQueueService.RunProgressState();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 2 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 100, Total = 100 }, state: state);

        // Model 1 of 2 fully complete = 50%, not 100%.
        Assert.Equal(50, vm.Progress);
    }

    [Fact]
    public void Progress_MultiModelPreset_Model2ReachesFullBar()
    {
        var vm = MakeVm();
        var state = new JobQueueService.RunProgressState();

        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 2 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 100, Total = 100 }, state: state);
        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 2, ModelCount = 2 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 100, Total = 100 }, state: state);

        Assert.Equal(100, vm.Progress);
    }

    [Fact]
    public void Progress_MultiModelPreset_Model2ProgressNotBlockedByModel1()
    {
        var vm = MakeVm();
        var state = new JobQueueService.RunProgressState();

        // Model 1 runs to completion (sets bar to 50%).
        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 1, ModelCount = 2 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 100, Total = 100 }, state: state);

        // Model 2 starts at 0 — progress must still advance from 50%.
        Invoke(
            vm,
            new PhaseProgress(JobPhase.LoadingModel) { ModelIndex = 2, ModelCount = 2 },
            state: state
        );
        Invoke(vm, new ProgressTick { Current = 10, Total = 100 }, state: state);

        Assert.True(vm.Progress > 50);
    }
}

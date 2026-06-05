using StemForge.Models;
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

    private static void Invoke(
        JobItemViewModel vm,
        JobProgress p,
        int presetIndex = 0,
        int totalSteps = 1,
        string presetLabel = "Vocals Clean"
    ) => JobQueueService.HandleProgress(vm, p, presetIndex, totalSteps, presetLabel);

    // ── loading_model emits a feed entry ────────────────────────────────────

    [Fact]
    public void LoadingModel_SingleModel_AppendsLoadingLineToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "loading_model",
                ModelIndex = 1,
                ModelCount = 1,
            }
        );

        Assert.Contains("Loading model", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_MultiModel_IncludesIndexInFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "loading_model",
                ModelIndex = 1,
                ModelCount = 2,
            }
        );

        Assert.Contains("Loading model 1/2", vm.LogOutput);
    }

    [Fact]
    public void LoadingModel_MultiPreset_IncludesPresetLabelInFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "loading_model",
                ModelIndex = 1,
                ModelCount = 1,
            },
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
            new JobProgress
            {
                Phase = "downloading_model",
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
            new JobProgress
            {
                Phase = "downloading_model",
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

        Invoke(vm, new JobProgress { Phase = "separating", ModelCount = 1 });

        Assert.Contains("Separating", vm.LogOutput);
    }

    [Fact]
    public void Separating_MultiModel_IncludesCountInFeed()
    {
        var vm = MakeVm();

        Invoke(vm, new JobProgress { Phase = "separating", ModelCount = 3 });

        Assert.Contains("3 models", vm.LogOutput);
    }

    // ── ensembling emits a feed entry ────────────────────────────────────────

    [Fact]
    public void Ensembling_WithStem_AppendsCombiningLineWithStem()
    {
        var vm = MakeVm();

        Invoke(vm, new JobProgress { Phase = "ensembling", Stem = "Vocals" });

        Assert.Contains("Combining Vocals", vm.LogOutput);
    }

    [Fact]
    public void Ensembling_WithoutStem_AppendsCombiningStemsLine()
    {
        var vm = MakeVm();

        Invoke(vm, new JobProgress { Phase = "ensembling" });

        Assert.Contains("Combining stems", vm.LogOutput);
    }

    // ── log phase filtering ──────────────────────────────────────────────────

    [Fact]
    public void Log_InfoLevel_DoesNotAppendToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "log",
                LogLevel = "info",
                LogMessage = "Input audio subtype: PCM_24",
            }
        );

        Assert.True(string.IsNullOrEmpty(vm.LogOutput));
    }

    [Fact]
    public void Log_WarningLevel_AppendsToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "log",
                LogLevel = "warning",
                LogMessage = "Something looks wrong",
            }
        );

        Assert.Contains("Something looks wrong", vm.LogOutput);
    }

    [Fact]
    public void Log_ErrorLevel_AppendsToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "log",
                LogLevel = "error",
                LogMessage = "Fatal error in model",
            }
        );

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

    // ── progress ticks do not produce feed entries ───────────────────────────

    [Fact]
    public void Progress_Tick_DoesNotAppendToFeed()
    {
        var vm = MakeVm();

        Invoke(
            vm,
            new JobProgress
            {
                Phase = "progress",
                Current = 50,
                Total = 100,
            }
        );

        Assert.True(string.IsNullOrEmpty(vm.LogOutput));
    }
}

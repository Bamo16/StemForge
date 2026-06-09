using System.Collections.ObjectModel;
using Avalonia.Threading;
using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Thin adapter over <see cref="SeparationPipeline"/>. Manages the job queue and maps
/// <see cref="JobUpdate"/> progress events onto <see cref="JobItemViewModel"/> properties.
/// One user-submitted job runs at a time; all others wait in FIFO order. Thread-safe
/// enqueue; all observable mutations happen on the UI thread.
/// </summary>
public sealed class JobQueueService(SeparationPipeline pipeline, AppSettings settings)
{
    private readonly SeparationPipeline _pipeline = pipeline;
    private readonly AppSettings _settings = settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _currentCts;
    private JobItemViewModel? _currentJob;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    public int ActiveCount =>
        Jobs.Count(job => job is { Status: JobStatus.Running or JobStatus.Queued });

    public void Enqueue(JobRecord record)
    {
        var vm = new JobItemViewModel(record, _settings.MaxJobLogLines)
        {
            CancelRequested = OnCancelRequested,
        };

        Dispatcher.UIThread.Post(() => Jobs.Add(vm));

        // Task.Run escapes the Avalonia SynchronizationContext so continuations after
        // _gate.WaitAsync() run on the thread pool, not through the UI dispatcher.
        _ = Task.Run(() => RunWhenReadyAsync(vm));
    }

    private async Task RunWhenReadyAsync(JobItemViewModel vm)
    {
        await _gate.WaitAsync();
        var cts = new CancellationTokenSource();
        _currentCts = cts;
        _currentJob = vm;

        Dispatcher.UIThread.Post(() =>
        {
            vm.Status = JobStatus.Running;
            vm.Progress = 0;
            vm.StatusText = "Starting…";
            OnPropertyChanged();
        });

        // Mutable state shared across progress callbacks (avoids ref-in-lambda restriction).
        var runState = new RunProgressState();

        // NOTE: Progress<T> captures SynchronizationContext at construction time.
        // This method runs on the thread-pool (via Task.Run), so Progress<T> has a null
        // SynchronizationContext — its callback therefore also runs on the thread-pool.
        // We must post explicitly to the UI thread inside the callback.
        var progress = new Progress<JobUpdate>(update =>
            Dispatcher.UIThread.Post(() => ApplyUpdate(vm, update, runState))
        );

        try
        {
            var outputFiles = await _pipeline.RunAsync(vm.Job, progress, cts.Token);
            Dispatcher.UIThread.Post(() =>
            {
                vm.OutputFiles.AddRange(outputFiles);
                vm.Progress = 100;
                vm.StatusText =
                    $"{outputFiles.Count} stem{(outputFiles.Count == 1 ? "" : "s")} written";
                vm.Status = JobStatus.Done;
                vm.IsExpanded = true;
                OnPropertyChanged();
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                vm.StatusText = "Cancelled";
                vm.Status = JobStatus.Cancelled;
                OnPropertyChanged();
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("job", $"Job failed — {ex.Message}");
            Dispatcher.UIThread.Post(() =>
            {
                vm.ErrorMessage = ex.Message;
                vm.StatusText = "Failed";
                vm.Status = JobStatus.Failed;
                vm.IsExpanded = true;
                vm.FlushRawLogToOutput();
                OnPropertyChanged();
            });
        }
        finally
        {
            _currentJob = null;
            _currentCts = null;
            _gate.Release();
        }
    }

    // ── Progress mapping ──────────────────────────────────────────────────────

    internal static void ApplyUpdate(JobItemViewModel vm, JobUpdate update, RunProgressState state)
    {
        var runLabel = update.RunLabel ?? "";
        var totalSteps = update.RunCount;
        var prefix = totalSteps > 1 ? $"[{runLabel}]" : runLabel;

        switch (update.Phase)
        {
            case "downloading":
                vm.Progress = 0;
                vm.StatusText = "Downloading…";
                break;

            case "starting":
                var presetIndex = update.RunIndex;
                vm.PresetCounter = totalSteps > 1 ? $"{presetIndex + 1}/{totalSteps}" : "";
                vm.StatusText = $"{runLabel} — Starting…";
                break;

            case "downloading_model":
                if (update.Cached == false)
                {
                    vm.StatusText =
                        update.ModelCount > 1
                            ? $"{runLabel} — Downloading model {update.ModelIndex}/{update.ModelCount}…"
                            : $"{runLabel} — Downloading model…";
                    var logLine =
                        update.ModelCount > 1
                            ? $"{prefix} Downloading model {update.ModelIndex}/{update.ModelCount}…"
                            : $"{prefix} Downloading model…";
                    vm.AppendLog(logLine);
                }
                break;

            case "loading_model":
                state.ModelIndex = update.ModelIndex ?? 1;
                state.ModelCount = update.ModelCount ?? 1;
                vm.StatusText =
                    update.ModelCount > 1
                        ? $"{runLabel} — Loading model {update.ModelIndex}/{update.ModelCount}…"
                        : $"{runLabel} — Loading model…";
                var modelName = update.Model is { Length: > 0 } m
                    ? Path.GetFileNameWithoutExtension(m)
                    : null;
                var loadLine = (modelName, update.ModelCount > 1) switch
                {
                    ({ } name, true) =>
                        $"{prefix} Loading {name} (model {update.ModelIndex}/{update.ModelCount})…",
                    ({ } name, false) => $"{prefix} Loading {name}…",
                    (null, true) =>
                        $"{prefix} Loading model {update.ModelIndex}/{update.ModelCount}…",
                    (null, false) => $"{prefix} Loading model…",
                };
                vm.AppendLog(loadLine);
                state.PendingRunLogLine = $"{prefix} Running…";
                break;

            case "separating":
                vm.StatusText =
                    update.ModelCount > 1
                        ? $"{runLabel} — Separating ({update.ModelCount} models)…"
                        : $"{runLabel} — Separating…";
                var sepLine =
                    update.ModelCount > 1
                        ? $"{prefix} Separating ({update.ModelCount} models)…"
                        : $"{prefix} Separating…";
                vm.AppendLog(sepLine);
                break;

            case "progress":
                if (state.PendingRunLogLine is { } runLine)
                {
                    vm.AppendLog(runLine);
                    state.PendingRunLogLine = null;
                }
                if (update.ProgressTotal is > 0 && update.ProgressCurrent is not null)
                {
                    vm.Progress = Math.Max(vm.Progress, update.OverallPercent);
                    var runningStatus =
                        state.ModelCount > 1
                            ? $"{runLabel} — Running model {state.ModelIndex}/{state.ModelCount}…"
                            : $"{runLabel} — Running…";
                    if (!vm.StatusText.Contains("Combining"))
                        vm.StatusText = runningStatus;
                }
                break;

            case "ensembling":
                vm.StatusText = update.Stem is { } stem
                    ? $"{runLabel} — Combining {stem}…"
                    : $"{runLabel} — Combining stems…";
                var ensLine = update.Stem is { } s
                    ? $"{prefix} Combining {s}…"
                    : $"{prefix} Combining stems…";
                vm.AppendLog(ensLine);
                break;

            case "stem_written":
                if (update.OutputPath is { } path)
                    vm.AppendLog(path);
                break;

            case "log":
                if (update.LogMessage is { Length: > 0 } msg)
                {
                    vm.AccumulateRawLog(msg);
                    if (update.LogLevel is "warning" or "error" && !msg.Contains("bits-per-sample"))
                        vm.AppendLog(msg);
                }
                break;

            case "run_complete":
                vm.Progress = update.OverallPercent;
                // Reset run-local model state for next run.
                state.ModelIndex = 1;
                state.ModelCount = 1;
                state.PendingRunLogLine = null;
                break;

            case "keep_source":
                vm.StatusText = "Saving source…";
                break;
        }
    }

    // ── Cancel / clear ────────────────────────────────────────────────────────

    private void OnCancelRequested(JobItemViewModel vm)
    {
        if (_currentJob == vm)
            _currentCts?.Cancel();
        else if (vm.Status == JobStatus.Queued)
        {
            Dispatcher.UIThread.Post(() =>
            {
                vm.Status = JobStatus.Cancelled;
                vm.StatusText = "Cancelled";
                OnPropertyChanged();
            });
        }
    }

    public void ClearDone()
    {
        var done = Jobs.Where(j => j.IsTerminal).ToList();
        foreach (var j in done)
            Jobs.Remove(j);
        OnPropertyChanged();
    }

    public event EventHandler? StateChanged;

    private void OnPropertyChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    // ── Run-local mutable state shared across progress callbacks ──────────────

    internal sealed class RunProgressState
    {
        public int ModelIndex { get; set; } = 1;
        public int ModelCount { get; set; } = 1;
        public string? PendingRunLogLine { get; set; }
    }
}

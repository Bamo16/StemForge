using System.Collections.ObjectModel;
using Avalonia.Threading;
using StemForge.Models;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Runs separation jobs sequentially. One job at a time; all others wait in FIFO order.
/// Thread-safe enqueue; all observable mutations happen on the UI thread.
/// </summary>
public sealed class JobQueueService
{
    private readonly SeparationService _separation;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _currentCts;
    private JobItemViewModel? _currentJob;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = new();

    public int ActiveCount => Jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Queued);

    public JobQueueService(SeparationService separation)
    {
        _separation = separation;
    }

    public void Enqueue(JobRecord record)
    {
        var vm = new JobItemViewModel(record);
        vm.CancelRequested = OnCancelRequested;

        Dispatcher.UIThread.Post(() => Jobs.Add(vm));

        _ = RunWhenReadyAsync(vm);
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

        try
        {
            var progress = new Progress<SeparationProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    vm.Progress = p.OverallPercent;
                    vm.StatusText = p.StatusText;
                });
            });

            await _separation.RunAsync(
                vm.Job.InputFilePath,
                vm.Job.Presets,
                vm.Job.OutputDir,
                SeparationService.ResolveModelsDir(),
                progress,
                cts.Token
            );

            // Collect output files written to OutputDir matching this job's presets
            var baseName = Path.GetFileNameWithoutExtension(vm.Job.InputFilePath);
            var outputFiles = vm.Job.Presets
                .Select(p => Path.Combine(vm.Job.OutputDir, $"{baseName} ({p.Id}).flac"))
                .Where(File.Exists)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                vm.OutputFiles.AddRange(outputFiles);
                vm.Progress = 100;
                vm.StatusText = $"{outputFiles.Count} stem{(outputFiles.Count == 1 ? "" : "s")} written";
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
            Dispatcher.UIThread.Post(() =>
            {
                vm.ErrorMessage = ex.Message;
                vm.StatusText = "Failed";
                vm.Status = JobStatus.Failed;
                vm.IsExpanded = true;
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

    private void OnCancelRequested(JobItemViewModel vm)
    {
        if (_currentJob == vm)
            _currentCts?.Cancel();
        else if (vm.Status == JobStatus.Queued)
        {
            // Job hasn't started yet — mark cancelled immediately
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
}

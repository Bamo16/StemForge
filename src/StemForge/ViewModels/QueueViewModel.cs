using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humanizer;
using StemForge.Services;

namespace StemForge.ViewModels;

public partial class QueueViewModel : PageViewModelBase
{
    private readonly JobQueueService _queue;

    public override string Title => "Job Queue";

    public ObservableCollection<JobItemViewModel> Jobs => _queue.Jobs;

    [ObservableProperty]
    public partial string SummaryLabel { get; set; } = string.Empty;

    public QueueViewModel(JobQueueService queue)
    {
        _queue = queue;
        _queue.StateChanged += (_, _) => RefreshSummary();
        _queue.Jobs.CollectionChanged += (_, _) => RefreshSummary();
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var total = _queue.Jobs.Count;
        var running = _queue.Jobs.Count(j => j.Status == JobStatus.Running);
        SummaryLabel =
            total == 0 ? "No jobs"
            : running > 0 ? $"{"job".ToQuantity(total)} · {running} running"
            : "job".ToQuantity(total);
    }

    [RelayCommand]
    private void ClearDone() => _queue.ClearDone();
}

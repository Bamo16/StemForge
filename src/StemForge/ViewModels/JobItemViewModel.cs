using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;

namespace StemForge.ViewModels;

public partial class JobItemViewModel : ObservableObject
{
    public JobRecord Job { get; }

    public string PresetSummary => Job.PresetSummary;

    [ObservableProperty]
    public partial string InputFileName { get; set; }

    private readonly int _maxLogLines;

    public JobItemViewModel(JobRecord job, int maxLogLines = 500)
    {
        Job = job;
        InputFileName = job.InputFileName;
        _maxLogLines = Math.Max(50, maxLogLines);
    }

    [ObservableProperty]
    public partial JobStatus Status { get; set; } = JobStatus.Queued;

    [ObservableProperty]
    public partial int Progress { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PresetCounter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
    public List<string> OutputFiles { get; } = [];

    private readonly Queue<string> _logLines = new();

    // Accumulates every raw "log" line from the driver regardless of level.
    // Exposed as LogOutput on failure so the user can see the full subprocess output.
    private readonly Queue<string> _rawLogLines = new();

    // Set by HandleProgress on loading_model; consumed (once) by the first progress tick
    // to emit a "Running model X/Y" entry after the model has actually started inference.
    internal string? PendingRunLogLine { get; set; }

    // Tracks which model within the current preset is running so the progress bar
    // advances proportionally across all models rather than pegging at 100% after model 1.
    internal int CurrentModelIndex { get; set; } = 1;
    internal int CurrentModelCount { get; set; } = 1;

    [ObservableProperty]
    public partial string LogOutput { get; set; } = string.Empty;

    /// <summary>Append a timeline line to the visible log feed. Must be called on the UI thread.</summary>
    public void AppendLog(string line)
    {
        _logLines.Enqueue(line);
        while (_logLines.Count > _maxLogLines)
            _logLines.Dequeue();
        LogOutput = string.Join('\n', _logLines);
    }

    /// <summary>
    /// Accumulate a raw driver log line without showing it in the feed.
    /// Must be called on the UI thread.
    /// </summary>
    public void AccumulateRawLog(string line)
    {
        _rawLogLines.Enqueue(line);
        while (_rawLogLines.Count > _maxLogLines)
            _rawLogLines.Dequeue();
    }

    /// <summary>
    /// Replace the visible LogOutput with the full accumulated raw log.
    /// Call this when a job fails so the user can see the full subprocess output.
    /// Must be called on the UI thread.
    /// </summary>
    public void FlushRawLogToOutput()
    {
        if (_rawLogLines.Count == 0)
            return;
        LogOutput = string.Join('\n', _rawLogLines);
    }

    public bool HasOutputFiles => OutputFiles.Count > 0;
    public bool IsRunning => Status == JobStatus.Running;
    public bool IsDone => Status == JobStatus.Done;
    public bool IsTerminal => Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled;
    public bool ShowProgress => Status is JobStatus.Running or JobStatus.Done;
    public string ProgressLabel =>
        IsRunning && !string.IsNullOrEmpty(PresetCounter) ? PresetCounter : "—";

    public IBrush StatusBrush =>
        Status switch
        {
            JobStatus.Running => new SolidColorBrush(Color.Parse("#d4703a")),
            JobStatus.Done => new SolidColorBrush(Color.Parse("#30d158")),
            JobStatus.Failed => new SolidColorBrush(Color.Parse("#ff453a")),
            _ => new SolidColorBrush(Color.Parse("#636366")),
        };

    public string StatusLabel =>
        Status switch
        {
            JobStatus.Queued => "QUEUED",
            JobStatus.Running => "RUNNING",
            JobStatus.Done => "DONE",
            JobStatus.Failed => "FAILED",
            JobStatus.Cancelled => "CANCELLED",
            _ => Status.ToString().ToUpperInvariant(),
        };

    public Action<JobItemViewModel>? CancelRequested { get; set; }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this);

    [RelayCommand]
    private void ShowInExplorer()
    {
        if (OutputFiles.Count == 0)
            return;
        // Select the first output file in Explorer
        var path = OutputFiles[0];
        if (File.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(Path.GetDirectoryName(path)))
            System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(path)!);
    }

    partial void OnStatusChanged(JobStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsTerminal));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(ProgressLabel));
    }

    partial void OnProgressChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
    }

    partial void OnPresetCounterChanged(string value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
    }
}

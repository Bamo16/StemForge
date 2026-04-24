using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;

namespace StemForge.ViewModels;

public partial class JobItemViewModel : ObservableObject
{
    public JobRecord Job { get; }

    // Forwarded from record for convenient binding
    public string InputFileName => Job.InputFileName;
    public string PresetSummary => Job.PresetSummary;

    [ObservableProperty]
    private JobStatus _status = JobStatus.Queued;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isExpanded;

    public List<string> OutputFiles { get; } = new();

    public bool HasOutputFiles => OutputFiles.Count > 0;
    public bool IsRunning => Status == JobStatus.Running;
    public bool IsDone => Status == JobStatus.Done;
    public bool IsTerminal => Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled;
    public bool ShowProgress => Status is JobStatus.Running or JobStatus.Done;
    public string ProgressLabel => ShowProgress ? $"{Progress}%" : "—";

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

    public JobItemViewModel(JobRecord job)
    {
        Job = job;
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this);

    [RelayCommand]
    private void ShowInExplorer()
    {
        if (OutputFiles.Count == 0) return;
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
}

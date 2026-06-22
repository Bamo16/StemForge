using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Services;

namespace StemForge.ViewModels;

public partial class LogsViewModel : PageViewModelBase
{
    private readonly AppLoggerSink _sink;

    public override string Title => "Logs";

    public ObservableCollection<LogEntry> Displayed { get; } = [];

    [ObservableProperty]
    public partial bool ShowDebug { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowInfo { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowWarning { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowError { get; set; } = true;

    public LogsViewModel(AppLoggerSink sink)
    {
        _sink = sink;
        Rebuild();
        _sink.Entries.CollectionChanged += OnSourceChanged;
    }

    partial void OnShowDebugChanged(bool value) => Rebuild();

    partial void OnShowInfoChanged(bool value) => Rebuild();

    partial void OnShowWarningChanged(bool value) => Rebuild();

    partial void OnShowErrorChanged(bool value) => Rebuild();

    [RelayCommand]
    private void ToggleDebug() => ShowDebug = !ShowDebug;

    [RelayCommand]
    private void ToggleInfo() => ShowInfo = !ShowInfo;

    [RelayCommand]
    private void ToggleWarning() => ShowWarning = !ShowWarning;

    [RelayCommand]
    private void ToggleError() => ShowError = !ShowError;

    [RelayCommand]
    private void Clear() => _sink.Entries.Clear();

    [RelayCommand]
    private void OpenLogsFolder()
    {
        if (AppLogger.LogDirectory is { Length: > 0 } dir && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (LogEntry entry in e.NewItems!)
                    if (Passes(entry))
                        Displayed.Add(entry);
                NotifyDisplayedText();
                break;
            default:
                Rebuild();
                break;
        }
    }

    public string DisplayedText =>
        string.Join(
            '\n',
            Displayed.Select(e =>
                $"{e.TimeDisplay, -12}  {e.LevelTag, -3}  {Truncate(e.Source, 24), -24}  {e.Message}"
            )
        );

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private void NotifyDisplayedText() => OnPropertyChanged(nameof(DisplayedText));

    private void Rebuild()
    {
        Displayed.Clear();
        foreach (var entry in _sink.Entries.Where(Passes))
            Displayed.Add(entry);
        NotifyDisplayedText();
    }

    private bool Passes(LogEntry entry) =>
        entry.Level switch
        {
            LogLevel.Debug => ShowDebug,
            LogLevel.Info => ShowInfo,
            LogLevel.Warning => ShowWarning,
            LogLevel.Error => ShowError,
            _ => true,
        };
}

using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace StemForge.Services;

/// <summary>
/// GUI-side sink for <see cref="AppLogger"/>. Owns the observable log buffer that the Logs view
/// binds to. Registers itself with <see cref="AppLogger.RegisterSink"/> on construction so all
/// log entries are marshalled onto the UI thread and appended here.
/// </summary>
public sealed class AppLoggerSink
{
    private readonly int _maxEntries;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public AppLoggerSink(int maxEntries = 2000)
    {
        _maxEntries = Math.Max(100, maxEntries);
        AppLogger.RegisterSink(Add);
    }

    private void Add(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
            AddOnUiThread(entry);
        else
            Dispatcher.UIThread.Post(() => AddOnUiThread(entry));
    }

    private void AddOnUiThread(LogEntry entry)
    {
        while (Entries.Count >= _maxEntries)
            Entries.RemoveAt(0);
        Entries.Add(entry);
    }
}

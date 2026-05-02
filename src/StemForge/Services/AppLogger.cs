using System.Collections.ObjectModel;
using Avalonia.Threading;
using StemForge.Extensions;

namespace StemForge.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Source,
    string Message
)
{
    public string TimeDisplay => Timestamp.ToString("HH:mm:ss.fff");

    public string LevelTag =>
        Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???",
        };

    public bool IsDebug => Level == LogLevel.Debug;
    public bool IsInfo => Level == LogLevel.Info;
    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level == LogLevel.Error;
}

public static class AppLogger
{
    private const int MaxEntries = 2000;
    private const int MaxLogFiles = 10;

    private static readonly object _fileLock = new();
    private static StreamWriter? _fileWriter;

    public static ObservableCollection<LogEntry> Entries { get; } = [];

    /// Directory where log files are written. Null until Initialize() is called.
    public static string? LogDirectory { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// Call once at startup. Creates a new dated log file and prunes old ones.
    public static void Initialize()
    {
        var dir = Environment.SpecialFolder.LocalApplicationData.GetFolderPath("StemForge", "logs");
        try
        {
            Directory.CreateDirectory(dir);
            LogDirectory = dir;

            // Keep only the most recent (MaxLogFiles - 1) existing files.
            var old = Directory
                .GetFiles(dir, "stemforge-*.log")
                .OrderByDescending(File.GetCreationTimeUtc)
                .Skip(MaxLogFiles - 1)
                .ToList();
            foreach (var f in old)
                try
                {
                    File.Delete(f);
                }
                catch { }

            var path = Path.Combine(dir, $"stemforge-{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _fileWriter = new StreamWriter(path, append: false, System.Text.Encoding.UTF8)
            {
                AutoFlush = true,
            };
            _fileWriter.WriteLine(
                $"# StemForge session started {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}"
            );
        }
        catch
        {
            // File logging is best-effort; never crash the app over it.
        }
    }

    /// Call on application exit to flush and close the log file.
    public static void Shutdown()
    {
        lock (_fileLock)
        {
            _fileWriter?.Dispose();
            _fileWriter = null;
        }
    }

    // ── Logging API ───────────────────────────────────────────────────────────

    public static void Debug(string source, string message) => Add(LogLevel.Debug, source, message);

    public static void Info(string source, string message) => Add(LogLevel.Info, source, message);

    public static void Warning(string source, string message) =>
        Add(LogLevel.Warning, source, message);

    public static void Error(string source, string message) => Add(LogLevel.Error, source, message);

    // ── Private ───────────────────────────────────────────────────────────────

    private static void Add(LogLevel level, string source, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, source, message);

        // Write to file on the calling thread (any thread); guarded by lock.
        lock (_fileLock)
        {
            try
            {
                _fileWriter?.WriteLine(
                    $"{entry.TimeDisplay, -12}  {entry.LevelTag, -3}  {Clip(source, 28), -28}  {message}"
                );
            }
            catch { }
        }

        // Push to the observable collection on the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
            AddOnUiThread(entry);
        else
            Dispatcher.UIThread.Post(() => AddOnUiThread(entry));
    }

    private static void AddOnUiThread(LogEntry entry)
    {
        if (Entries.Count >= MaxEntries)
            Entries.RemoveAt(0);
        Entries.Add(entry);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}

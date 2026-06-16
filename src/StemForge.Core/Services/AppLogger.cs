using System.Text.RegularExpressions;
using StemForge.Core.Extensions;

namespace StemForge.Core.Services;

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

public static partial class AppLogger
{
    private const int MaxLogFiles = 10;

    private static readonly Lock _fileLock = new();
    private static StreamWriter? _fileWriter;

    private static readonly List<Action<LogEntry>> _sinks = [];

    /// Directory where log files are written. Null until Initialize() is called.
    public static string? LogDirectory { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// Register a callback invoked on every log entry (called on the logging thread).
    public static void RegisterSink(Action<LogEntry> sink)
    {
        lock (_sinks)
            _sinks.Add(sink);
    }

    /// Call once at startup. Creates a new dated log file and prunes old ones.
    public static void Initialize(int maxEntries = 2000)
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
        var entry = new LogEntry(DateTimeOffset.Now, level, source, Redact(message));

        // Write to file on the calling thread (any thread); guarded by lock.
        lock (_fileLock)
        {
            try
            {
                _fileWriter?.WriteLine(
                    $"{entry.TimeDisplay, -12}  {entry.LevelTag, -3}  {Clip(source, 28), -28}  {entry.Message}"
                );
            }
            catch { }
        }

        // Dispatch to registered sinks (e.g. the GUI's observable-collection buffer).
        List<Action<LogEntry>> sinks;
        lock (_sinks)
            sinks = [.. _sinks];
        foreach (var sink in sinks)
            sink(entry);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    // Masks the value of an "ip=" query parameter in logged URLs (e.g. the public IP embedded in
    // a googlevideo media URL passed to ffmpeg). Logs reach the on-disk file and may be shared in
    // bug reports, so the IP is scrubbed at the sink, covering every channel at once. Scoped to the
    // ip= parameter so unrelated addresses in other messages are left intact for troubleshooting.
    [GeneratedRegex(@"(?<=[?&]ip=)[^&\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex IpParamPattern();

    internal static string Redact(string message) =>
        message.Contains("ip=", StringComparison.OrdinalIgnoreCase)
            ? IpParamPattern().Replace(message, "<REDACTED>")
            : message;
}

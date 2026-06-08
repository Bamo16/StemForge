namespace StemForge.Core.Services;

public interface IProcessRunner
{
    Task<ProcessRunner.Result> RunAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default,
        bool logRawLines = true
    );

    Task<ProcessRunner.Result> RunCheckedAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default,
        bool logRawLines = true
    );

    Task RunStreamingAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        bool logRawLines = true
    );

    /// Captures stdout into the returned Result while streaming stderr lines live to
    /// <paramref name="stderrProgress"/>. Throws on non-zero exit.
    Task<ProcessRunner.Result> RunStreamingStderrAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? stderrProgress = null,
        CancellationToken ct = default,
        bool logRawLines = true
    );
}

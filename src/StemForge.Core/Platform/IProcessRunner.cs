namespace StemForge.Core.Platform;

public interface IProcessRunner
{
    Task<ProcessRunner.Result> RunAsync(
        string exe,
        IEnumerable<string> args,
        bool logRawLines = true,
        CancellationToken ct = default
    );

    Task<ProcessRunner.Result> RunCheckedAsync(
        string exe,
        IEnumerable<string> args,
        bool logRawLines = true,
        CancellationToken ct = default
    );

    Task RunStreamingAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? progress = null,
        bool logRawLines = true,
        CancellationToken ct = default
    );

    /// Captures stdout into the returned Result while streaming stderr lines live to
    /// <paramref name="stderrProgress"/>. Throws on non-zero exit.
    Task<ProcessRunner.Result> RunStreamingStderrAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? stderrProgress = null,
        bool logRawLines = true,
        CancellationToken ct = default
    );
}

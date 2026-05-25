using System.Diagnostics;
using System.Text;

namespace StemForge.Services;

/// <summary>
/// Unified helper for spawning external processes. Four entry points:
///   RunAsync              — capture stdout+stderr, no throw on non-zero exit
///   RunCheckedAsync       — same, but throws ProcessFailedException on non-zero exit
///   RunStreamingAsync     — stream all lines live via IProgress, throws on non-zero exit
///   RunStreamingStderrAsync — capture stdout silently, stream stderr live, throws on non-zero exit
/// All overloads drain both stdout and stderr to prevent OS pipe-buffer deadlocks.
/// Cancellation kills the entire process tree; the token is checked after exit.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    // ── Result ──────────────────────────────────────────────────────────────────

    public sealed record Result(int ExitCode, string Stdout, string Stderr)
    {
        public bool Success => ExitCode == 0;

        /// Stdout if non-empty, otherwise Stderr. Some CLIs (e.g. audio-separator)
        /// write version info to stderr via Python logging.
        public string Output => string.IsNullOrWhiteSpace(Stdout) ? Stderr : Stdout;
    }

    // ── Exception ───────────────────────────────────────────────────────────────

    public sealed class ProcessFailedException(string exe, int exitCode, string stderr)
        : Exception(
            string.IsNullOrWhiteSpace(stderr)
                ? $"'{Path.GetFileName(exe)}' exited with code {exitCode}"
                : $"'{Path.GetFileName(exe)}' exited with code {exitCode}:\n{stderr}"
        )
    {
        public int ExitCode { get; } = exitCode;
        public string Stderr { get; } = stderr;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// Run to completion and capture output. Returns the result regardless of exit code.
    public Task<Result> RunAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default,
        bool logRawLines = true
    ) => CoreAsync(exe, args, progress: null, throwOnFailure: false, logRawLines, ct);

    /// Run to completion and capture output. Throws <see cref="ProcessFailedException"/>
    /// if the process exits with a non-zero code.
    public Task<Result> RunCheckedAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default,
        bool logRawLines = true
    ) => CoreAsync(exe, args, progress: null, throwOnFailure: true, logRawLines, ct);

    /// Run and stream each line (stdout + stderr) to <paramref name="progress"/> as it arrives.
    /// Throws <see cref="ProcessFailedException"/> on non-zero exit.
    public async Task RunStreamingAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        bool logRawLines = true
    ) => await CoreAsync(exe, args, progress, throwOnFailure: true, logRawLines, ct);

    /// Captures stdout into the returned <see cref="Result"/> while streaming stderr lines live
    /// to <paramref name="stderrProgress"/>. Throws <see cref="ProcessFailedException"/> on non-zero exit.
    public Task<Result> RunStreamingStderrAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? stderrProgress = null,
        CancellationToken ct = default,
        bool logRawLines = true
    ) =>
        CoreAsync(
            exe,
            args,
            stderrProgress,
            throwOnFailure: true,
            logRawLines,
            ct,
            captureStdout: true
        );

    // ── Core ────────────────────────────────────────────────────────────────────

    private static async Task<Result> CoreAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? progress,
        bool throwOnFailure,
        bool logRawLines,
        CancellationToken ct,
        bool captureStdout = false
    )
    {
        var argList = args as IReadOnlyList<string> ?? [.. args];
        var exeName = Path.GetFileName(exe);

        AppLogger.Debug("Process", $"→ {exeName} {string.Join(' ', argList)}");

        var startInfo = new ProcessStartInfo(exe, argList)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        using var p =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{exeName}'");

        // Kill the entire tree when the token fires; reads below complete once streams close.
        using var _ = ct.Register(() =>
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch { }
        });

        string stdout,
            stderr;

        if (progress is not null)
        {
            if (captureStdout)
            {
                // Mixed mode: capture stdout silently, stream stderr live.
                var stdoutLines = new List<string>();
                await foreach (var line in p.ReadAllLinesAsync(CancellationToken.None))
                {
                    if (line.StandardError)
                    {
                        progress.Report(line.Content);
                        if (logRawLines)
                            AppLogger.Debug($"{exeName}.err", line.Content);
                    }
                    else
                    {
                        stdoutLines.Add(line.Content);
                    }
                }
                stdout = string.Join('\n', stdoutLines).Trim();
                stderr = string.Empty;
            }
            else
            {
                // Stream mode: report all lines (stdout + stderr) as they arrive.
                await foreach (var line in p.ReadAllLinesAsync(CancellationToken.None))
                {
                    progress.Report(line.Content);
                    if (logRawLines)
                        AppLogger.Debug(
                            line.StandardError ? $"{exeName}.err" : $"{exeName}.out",
                            line.Content
                        );
                }
                stdout = stderr = string.Empty;
            }
        }
        else
        {
            // Capture mode: ReadAllTextAsync drains both streams concurrently to avoid pipe deadlocks.
            (stdout, stderr) = await p.ReadAllTextAsync(CancellationToken.None);
            stdout = stdout.Trim();
            stderr = stderr.Trim();

            if (logRawLines)
            {
                // stdout captured for programmatic use (e.g. JSON blobs) is never raw-logged.
                if (!captureStdout)
                    foreach (
                        var line in stdout.Split(
                            '\n',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                    )
                        AppLogger.Debug($"{exeName}.out", line);
                foreach (
                    var line in stderr.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                )
                    AppLogger.Debug($"{exeName}.err", line);
            }
        }

        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();

        var result = new Result(p.ExitCode, stdout, stderr);
        if (!result.Success)
            AppLogger.Error(
                "Process",
                $"← {exeName} exit {p.ExitCode}"
                    + (string.IsNullOrWhiteSpace(stderr) ? "" : $"\n{stderr}")
            );
        else
            AppLogger.Info("Process", $"← {exeName} exit 0");

        if (throwOnFailure && !result.Success)
            throw new ProcessFailedException(exe, p.ExitCode, stderr);

        return result;
    }
}

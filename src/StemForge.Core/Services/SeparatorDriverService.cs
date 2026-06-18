using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StemForge.Core.Extensions;
using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// Manages a single long-lived <c>separator_driver.py</c> process.
/// Spawns lazily on first use, tears down after <see cref="IdleTimeout"/> of inactivity,
/// and respawns transparently on the next call. Only one separation job runs at a time.
/// </summary>
public sealed class SeparatorDriverService(AppPaths paths) : ISeparatorDriverService
{
    private readonly AppPaths _paths = paths;

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    // ── Process state ────────────────────────────────────────────────────────

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _readerTask;
    private Task? _stderrTask;

    // Recent driver output, retained so an unexpected exit can be reported with
    // context. Stderr carries python warnings/tracebacks; the activity tail
    // carries the structured log lines, which are the only "where it died" signal
    // for a native crash that writes nothing to stderr (e.g. a 0xC0000409 abort
    // inside the inference engine).
    private readonly BoundedLineBuffer _stderrTail = new(capacity: 30);
    private readonly BoundedLineBuffer _activityTail = new(capacity: 10);

    // ── Synchronisation ──────────────────────────────────────────────────────

    private readonly SemaphoreSlim _spawnLock = new(1, 1); // guards spawn/teardown
    private readonly SemaphoreSlim _stdinLock = new(1, 1); // serialises stdin writes
    private readonly SemaphoreSlim _runLock = new(1, 1); // one job at a time

    // ── Ready handshake ──────────────────────────────────────────────────────

    private TaskCompletionSource? _readyTcs;

    // ── Active job ───────────────────────────────────────────────────────────

    private volatile ActiveJob? _activeJob;

    private sealed class ActiveJob
    {
        public required string Id { get; init; }
        public TaskCompletionSource<JobResult> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelAcknowledged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<JobProgress>? Progress { get; init; }
    }

    // ── Idle timer ───────────────────────────────────────────────────────────

    private CancellationTokenSource? _idleCts;

    // ── Public API ───────────────────────────────────────────────────────────

    public event Action<IReadOnlyList<Preset>>? PresetsLoaded;

    public async Task<JobResult> RunAsync(
        JobRequest request,
        IProgress<JobProgress>? progress,
        CancellationToken ct
    )
    {
        CancelIdleTimer();

        await _runLock.WaitAsync(ct);
        try
        {
            await EnsureReadyAsync(ct);

            var jobId = $"job_{Guid.NewGuid():N}";
            var job = new ActiveJob { Id = jobId, Progress = progress };
            _activeJob = job;

            await SendCommandAsync(BuildRunCommand(jobId, request), ct);

            try
            {
                return await job.Tcs.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Ask the driver to stop the current job cleanly rather than
                // killing the whole process. Fall back to killing if it doesn't
                // respond within 10 s (e.g. stuck in non-interruptible I/O).
                if (!job.Tcs.Task.IsCompleted)
                {
                    try
                    {
                        await SendCommandAsync(
                            new { cmd = "cancel", id = jobId },
                            CancellationToken.None
                        );
                    }
                    catch { }

                    using var ackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await job.CancelAcknowledged.Task.WaitAsync(ackTimeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        AppLogger.Warning("driver", "Cancel ack timeout — killing driver process");
                        try
                        {
                            _process?.Kill(entireProcessTree: true);
                        }
                        catch { }
                    }
                }
                throw;
            }
        }
        finally
        {
            _activeJob = null;
            _runLock.Release();
            RestartIdleTimer();
        }
    }

    // ── Spawn / teardown ─────────────────────────────────────────────────────

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        await _spawnLock.WaitAsync(ct);
        try
        {
            if (_process is { HasExited: false })
                return;

            DisposeProcess();

            _stderrTail.Clear();
            _activityTail.Clear();
            _readyTcs = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            var driverArgs = new List<string>
            {
                AppPaths.SeparationDriverScript,
                "--model-dir",
                _paths.ModelsDirectory,
                "--log-level",
                "info",
            };
            var ffmpegPath = _paths.Ffmpeg;
            if (Path.IsPathRooted(ffmpegPath))
            {
                driverArgs.Add("--ffmpeg-path");
                driverArgs.Add(ffmpegPath);
            }

            var startInfo = new ProcessStartInfo(_paths.SeparationDriverPython, driverArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }.WithEnvironmentVariables(("PYTHONIOENCODING", "utf-8"), ("PYTHONUTF8", "1"));

            AppLogger.Debug(
                "driver",
                $"Spawning: {startInfo.FileName} {AppPaths.SeparationDriverScript}"
            );
            _process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start separator driver");

            _stdin = _process.StandardInput;

            // Task.Run gives each read loop a thread-pool thread with no SynchronizationContext.
            // Without it, ReadLineAsync continuations would post back to the Avalonia dispatcher,
            // serializing all driver output through the UI queue.
            _stderrTask = Task.Run(
                async () =>
                {
                    while (await _process.StandardError.ReadLineAsync() is { } line)
                    {
                        AppLogger.Debug("driver.stderr", line);
                        _stderrTail.Add(line);
                    }
                },
                CancellationToken.None
            );

            _readerTask = Task.Run(ReadLoopAsync, CancellationToken.None);

            // Wait for the ready event (up to 2 min for CUDA init).
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            await _readyTcs.Task.WaitAsync(linked.Token);
        }
        finally
        {
            _spawnLock.Release();
        }
    }

    private async Task TeardownAsync()
    {
        await _spawnLock.WaitAsync();
        try
        {
            if (_process is null)
                return;

            try
            {
                // Ask the driver to exit cleanly.
                await SendCommandAsync(new { cmd = "shutdown" }, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(cts.Token);
            }
            catch { }
            finally
            {
                DisposeProcess();
            }
        }
        finally
        {
            _spawnLock.Release();
        }
    }

    private void DisposeProcess()
    {
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
        _stdin?.Dispose();
        _stdin = null;
    }

    // ── Event reader loop ────────────────────────────────────────────────────

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _process!.StandardOutput.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                DispatchEvent(line);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug("driver", $"Reader loop ended: {ex.Message}");
        }
        finally
        {
            // EOF or exception — fault any waiting TCS so callers don't hang.
            // Stderr is drained on its own loop; give it a brief moment to flush
            // the dying process's final lines into the tail buffer before we read it.
            try
            {
                _stderrTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            var exitCode = TryReadExitCode();
            var message = BuildTerminationMessage(
                exitCode,
                _stderrTail.Snapshot(),
                _activityTail.Snapshot()
            );

            _readyTcs?.TrySetException(new InvalidOperationException(message));
            _activeJob?.Tcs.TrySetException(new InvalidOperationException(message));
        }
    }

    private int? TryReadExitCode()
    {
        try
        {
            var process = _process;
            if (process is { HasExited: true })
                return process.ExitCode;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Builds the message used to fault a job when the driver process ends before
    /// the job completed: the exit code (when known), the tail of the driver's
    /// stderr (python warnings/tracebacks), and the tail of the structured activity
    /// log. The activity tail matters for a native crash that writes nothing to
    /// stderr, where it is the only record of what the driver was doing when it died.
    /// </summary>
    private static string BuildTerminationMessage(
        int? exitCode,
        IReadOnlyList<string> stderrTail,
        IReadOnlyList<string> activityTail
    )
    {
        var code = exitCode is { } c ? c.ToString() : "unknown";
        var sections = new List<string> { $"Driver exited (code {code})." };
        if (stderrTail.Count > 0)
            sections.Add($"Last error output: {string.Join("\n", stderrTail)}");
        if (activityTail.Count > 0)
            sections.Add($"Last activity: {string.Join("\n", activityTail)}");
        if (stderrTail.Count == 0 && activityTail.Count == 0)
            sections.Add("(no output)");
        return string.Join(" ", sections);
    }

    private void DispatchEvent(string line)
    {
        // Transport noise: a long-running native dependency (torch, onnxruntime, a stray print)
        // can leak a line to stdout that was never meant as a protocol message. That is not a
        // contract violation, so tolerate it and keep reading. Protocol lines are always a JSON
        // object, so anything not starting with '{' is noise.
        var trimmed = line.AsSpan().TrimStart();
        if (trimmed.IsEmpty || trimmed[0] != '{')
        {
            AppLogger.Warning("driver", $"Non-JSON from driver: {line}");
            return;
        }

        DriverEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize(line, DriverJsonContext.Default.DriverEvent);
        }
        catch (Exception ex)
        {
            // A JSON object the host cannot dispatch (unknown event/phase, or a malformed payload)
            // is a contract violation: the driver is co-versioned with this app, so this means a
            // protocol bug, not version skew. Fail loud — assert in dev/test, log in release — to
            // mirror the emit() assertion on the Python side.
            AppLogger.Error("driver", $"Driver protocol violation: {ex.Message} -- {line}");
            Debug.Fail($"Driver protocol violation: {line}");
            return;
        }

        if (evt is null)
            return;

        var job = _activeJob;

        switch (evt)
        {
            case ReadyEvent ready:
                AppLogger.Info(
                    "driver",
                    $"Ready — audio-separator {ready.SeparatorVersion ?? "?"} on {ready.Device ?? "?"}"
                );
                _readyTcs?.TrySetResult();
                _ = SendCommandAsync(new { cmd = "list_presets" }, CancellationToken.None);
                break;

            case PresetsEvent { Presets: { Count: > 0 } entries }:
            {
                var presets = DriverPresetCatalog.ToPresets(entries);
                if (presets.Count > 0)
                    PresetsLoaded?.Invoke(presets);
                break;
            }

            case PhaseEvent phase:
                job?.Progress?.Report(MapPhase(phase));
                break;

            case ProgressEvent progress:
                job?.Progress?.Report(
                    new JobProgress
                    {
                        Phase = "progress",
                        Current = progress.Current ?? 0,
                        Total = progress.Total,
                        Final = progress.Final,
                    }
                );
                break;

            case LogEvent log:
            {
                var level = log.Level ?? "info";
                var msg = log.Message ?? "";
                LogDriverMessage(level, msg);
                // Retain the structured log as the activity tail: on a native crash
                // (no stderr) this is the only record of what the driver was doing.
                if (msg.Length > 0)
                    _activityTail.Add(msg);
                job?.Progress?.Report(
                    new JobProgress
                    {
                        Phase = "log",
                        LogLevel = level,
                        LogMessage = msg,
                    }
                );
                break;
            }

            case StemWrittenEvent { } stemWritten:
            {
                var stem = stemWritten.Stem ?? "";
                var path = stemWritten.Path ?? "";
                AppLogger.Info("driver", $"stem_written: {stem} → {path}");
                job?.Progress?.Report(
                    new JobProgress
                    {
                        Phase = "stem_written",
                        OutputStem = stem,
                        OutputPath = path,
                    }
                );
                break;
            }

            case JobCompletedEvent completed when job is not null:
            {
                var outputs = ToJobOutputs(completed.Outputs);
                var discarded = ToJobOutputs(completed.Discarded);
                var dur = completed.DurationSeconds ?? 0;
                AppLogger.Info("driver", $"Job done in {dur:F1}s — {outputs.Count} stem(s)");
                job.Tcs.TrySetResult(new JobResult(true, outputs, discarded, dur, null, null));
                break;
            }

            case JobCancelledEvent when job is not null:
                AppLogger.Debug("driver", "Job cancelled by driver");
                job.CancelAcknowledged.TrySetResult();
                job.Tcs.TrySetCanceled();
                break;

            case JobFailedEvent failed when job is not null:
            {
                var err = failed.Error ?? "unknown";
                AppLogger.Error("driver", $"Job failed: {err}");
                if (failed.Traceback is { Length: > 0 } tb)
                    AppLogger.Debug("driver.tb", tb);
                job.Tcs.TrySetResult(new JobResult(false, [], [], 0, err, failed.Traceback));
                break;
            }

            case ErrorEvent error:
                AppLogger.Warning("driver", $"Driver error: {error.Error ?? ""}");
                break;

            case ByeEvent:
                AppLogger.Debug("driver", "Driver exited cleanly");
                break;
        }
    }

    // The Phase string here is the legacy JobProgress label the GUI timeline and CLI match on, not
    // the wire discriminator (now the typed DriverPhase). Reshaping JobProgress to carry the phase
    // as a first-class value is tracked separately (see the JobProgress.Phase follow-up).
    private static JobProgress MapPhase(PhaseEvent evt) =>
        evt.Phase switch
        {
            DriverPhase.DownloadingModel => new JobProgress
            {
                Phase = "downloading_model",
                Model = evt.Model,
                ModelIndex = evt.ModelIndex,
                ModelCount = evt.ModelCount,
                Cached = evt.Cached,
            },
            DriverPhase.LoadingModel => new JobProgress
            {
                Phase = "loading_model",
                Model = evt.Model,
                ModelIndex = evt.ModelIndex,
                ModelCount = evt.ModelCount,
            },
            DriverPhase.Ensembling => new JobProgress { Phase = "ensembling", Stem = evt.Stem },
            DriverPhase.Separating => new JobProgress
            {
                Phase = "separating",
                ModelCount = evt.ModelCount,
            },
            _ => new JobProgress { Phase = evt.Phase.ToString() },
        };

    private static List<JobOutput> ToJobOutputs(List<DriverJobOutput>? items)
    {
        var result = new List<JobOutput>();
        if (items is null)
            return result;
        foreach (var item in items)
        {
            var path = item.Path ?? "";
            if (path.Length > 0)
                result.Add(new JobOutput(item.Stem ?? "", path));
        }
        return result;
    }

    // ── stdin ────────────────────────────────────────────────────────────────

    private async Task SendCommandAsync(object command, CancellationToken ct)
    {
        await _stdinLock.WaitAsync(ct);
        try
        {
            if (_stdin is null)
                throw new InvalidOperationException("Driver stdin not available");
            var line = JsonSerializer.Serialize(command);
            await _stdin.WriteLineAsync(line.AsMemory(), ct);
            await _stdin.FlushAsync(ct);
        }
        finally
        {
            _stdinLock.Release();
        }
    }

    private static object BuildRunCommand(string jobId, JobRequest req) =>
        req switch
        {
            { PresetId: { } preset } => new
            {
                cmd = "run",
                id = jobId,
                audio = req.AudioPath,
                output_dir = req.OutputDir,
                output_format = req.OutputFormat,
                preset,
                custom_names = req.CustomOutputNames,
            },
            { Weights: { Count: > 0 } weights } => new
            {
                cmd = "run",
                id = jobId,
                audio = req.AudioPath,
                output_dir = req.OutputDir,
                output_format = req.OutputFormat,
                models = req.Models,
                algorithm = req.Algorithm ?? "avg_wave",
                weights,
            },
            _ => new
            {
                cmd = "run",
                id = jobId,
                audio = req.AudioPath,
                output_dir = req.OutputDir,
                output_format = req.OutputFormat,
                models = req.Models,
                algorithm = req.Algorithm ?? "avg_wave",
            },
        };

    // ── Idle timer ───────────────────────────────────────────────────────────

    private void RestartIdleTimer()
    {
        _idleCts?.Cancel();
        _idleCts?.Dispose();
        var cts = new CancellationTokenSource();
        _idleCts = cts;
        // Task.Run ensures TeardownAsync runs on the thread pool after the delay, not on
        // whichever SynchronizationContext was current at the call site.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(IdleTimeout, cts.Token);
                    AppLogger.Debug("driver", "Idle timeout — tearing down driver");
                    await TeardownAsync();
                }
                catch (OperationCanceledException) { }
            },
            CancellationToken.None
        );
    }

    private void CancelIdleTimer()
    {
        _idleCts?.Cancel();
        _idleCts?.Dispose();
        _idleCts = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void LogDriverMessage(string level, string message)
    {
        switch (level)
        {
            case "debug":
                AppLogger.Debug("separator", message);
                break;
            case "warning":
                AppLogger.Warning("separator", message);
                break;
            case "error":
                AppLogger.Error("separator", message);
                break;
            default:
                AppLogger.Info("separator", message);
                break;
        }
    }

    // ── Bounded line buffer ──────────────────────────────────────────────────

    /// <summary>
    /// Fixed-capacity ring buffer of the most recent lines. Once full, adding a
    /// new line evicts the oldest, so memory use stays bounded regardless of how
    /// much the driver writes. Used for both the stderr tail and the activity tail.
    /// </summary>
    private sealed class BoundedLineBuffer(int capacity)
    {
        private readonly int _capacity = capacity;
        private readonly Queue<string> _lines = new(capacity);
        private readonly Lock _gate = new();

        public void Add(string line)
        {
            lock (_gate)
            {
                if (_lines.Count >= _capacity)
                    _lines.Dequeue();
                _lines.Enqueue(line);
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate)
                return [.. _lines];
        }

        public void Clear()
        {
            lock (_gate)
                _lines.Clear();
        }
    }

    // ── Test seams ───────────────────────────────────────────────────────────
    // The dispatch path normally runs only against a live driver process. These seams let the
    // characterization tests install an observable active job and feed raw event lines through the
    // exact same dispatcher the read loop uses, without spawning Python.

    internal (Task<JobResult> Result, Task CancelAcknowledged) BeginJobForTest(
        string id,
        IProgress<JobProgress>? progress
    )
    {
        var job = new ActiveJob { Id = id, Progress = progress };
        _activeJob = job;
        return (job.Tcs.Task, job.CancelAcknowledged.Task);
    }

    internal Task ArmReadyForTest()
    {
        _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _readyTcs.Task;
    }

    internal void DispatchLineForTest(string line) => DispatchEvent(line);

    internal IReadOnlyList<string> ActivityTailForTest() => _activityTail.Snapshot();

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        CancelIdleTimer();
        await TeardownAsync();
        _spawnLock.Dispose();
        _stdinLock.Dispose();
        _runLock.Dispose();
    }
}

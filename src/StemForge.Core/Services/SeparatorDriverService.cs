using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                        AppLogger.Debug("driver.stderr", line);
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
            _readyTcs?.TrySetException(
                new InvalidOperationException("Driver process ended before emitting ready")
            );
            _activeJob?.Tcs.TrySetException(
                new InvalidOperationException("Driver process terminated unexpectedly")
            );
        }
    }

    private void DispatchEvent(string line)
    {
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(line);
        }
        catch
        {
            AppLogger.Warning("driver", $"Non-JSON from driver: {line}");
            return;
        }

        if (!root.TryGetProperty("event", out var evtProp))
            return;

        var evt = evtProp.GetString();
        var job = _activeJob;

        switch (evt)
        {
            case "ready":
            {
                var device = root.TryGetProperty("device", out var d) ? d.GetString() : "?";
                var ver = root.TryGetProperty("separator_version", out var v) ? v.GetString() : "?";
                AppLogger.Info("driver", $"Ready — audio-separator {ver} on {device}");
                _readyTcs?.TrySetResult();
                _ = SendCommandAsync(new { cmd = "list_presets" }, CancellationToken.None);
                break;
            }

            case "presets":
            {
                if (!root.TryGetProperty("presets", out var presetsEl))
                    break;
                var presets = ParsePresetsFromJson(presetsEl);
                if (presets.Count > 0)
                    PresetsLoaded?.Invoke(presets);
                break;
            }

            case "phase":
            {
                var phase = root.TryGetProperty("phase", out var p) ? p.GetString() ?? "" : "";
                job?.Progress?.Report(MapPhase(phase, root));
                break;
            }

            case "progress":
            {
                if (job is null)
                    break;
                var current = root.TryGetProperty("current", out var c) ? c.GetInt32() : 0;
                var total =
                    root.TryGetProperty("total", out var t) && t.ValueKind != JsonValueKind.Null
                        ? t.GetInt32()
                        : (int?)null;
                var final =
                    root.TryGetProperty("final", out var f) && f.ValueKind != JsonValueKind.Null
                        ? f.GetBoolean()
                        : (bool?)null;
                job.Progress?.Report(
                    new JobProgress
                    {
                        Phase = "progress",
                        Current = current,
                        Total = total,
                        Final = final,
                    }
                );
                break;
            }

            case "log":
            {
                var level = root.TryGetProperty("level", out var l)
                    ? l.GetString() ?? "info"
                    : "info";
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                LogDriverMessage(level, msg);
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

            case "stem_written":
            {
                var stem = root.TryGetProperty("stem", out var s) ? s.GetString() ?? "" : "";
                var path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
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

            case "job_completed":
            {
                if (job is null)
                    break;
                var outputs = ParseOutputList(root, "outputs");
                var discarded = ParseOutputList(root, "discarded");
                var dur = root.TryGetProperty("duration_seconds", out var d) ? d.GetDouble() : 0;
                AppLogger.Info("driver", $"Job done in {dur:F1}s — {outputs.Count} stem(s)");
                job.Tcs.TrySetResult(new JobResult(true, outputs, discarded, dur, null, null));
                break;
            }

            case "job_cancelled":
            {
                if (job is null)
                    break;
                AppLogger.Debug("driver", "Job cancelled by driver");
                job.CancelAcknowledged.TrySetResult();
                job.Tcs.TrySetCanceled();
                break;
            }

            case "job_failed":
            {
                if (job is null)
                    break;
                var err = root.TryGetProperty("error", out var e)
                    ? e.GetString() ?? "unknown"
                    : "unknown";
                var tb = root.TryGetProperty("traceback", out var t) ? t.GetString() : null;
                AppLogger.Error("driver", $"Job failed: {err}");
                if (tb is { Length: > 0 })
                    AppLogger.Debug("driver.tb", tb);
                job.Tcs.TrySetResult(new JobResult(false, [], [], 0, err, tb));
                break;
            }

            case "error":
            {
                var err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                AppLogger.Warning("driver", $"Driver error: {err}");
                break;
            }

            case "bye":
                AppLogger.Debug("driver", "Driver exited cleanly");
                break;
        }
    }

    private static JobProgress MapPhase(string phase, JsonElement root) =>
        phase switch
        {
            "downloading_model" => new JobProgress
            {
                Phase = phase,
                Model = root.TryGetProperty("model", out var m) ? m.GetString() : null,
                ModelIndex = root.TryGetProperty("model_index", out var mi) ? mi.GetInt32() : null,
                ModelCount = root.TryGetProperty("model_count", out var mc) ? mc.GetInt32() : null,
                Cached = root.TryGetProperty("cached", out var c) ? c.GetBoolean() : null,
            },
            "loading_model" => new JobProgress
            {
                Phase = phase,
                Model = root.TryGetProperty("model", out var m) ? m.GetString() : null,
                ModelIndex = root.TryGetProperty("model_index", out var mi) ? mi.GetInt32() : null,
                ModelCount = root.TryGetProperty("model_count", out var mc) ? mc.GetInt32() : null,
            },
            "ensembling" => new JobProgress
            {
                Phase = phase,
                Stem = root.TryGetProperty("stem", out var s) ? s.GetString() : null,
            },
            "separating" => new JobProgress
            {
                Phase = phase,
                ModelCount = root.TryGetProperty("model_count", out var mc) ? mc.GetInt32() : null,
            },
            _ => new JobProgress { Phase = phase },
        };

    private static List<JobOutput> ParseOutputList(JsonElement root, string key)
    {
        var result = new List<JobOutput>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in arr.EnumerateArray())
        {
            var stem = item.TryGetProperty("stem", out var s) ? s.GetString() ?? "" : "";
            var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            if (path.Length > 0)
                result.Add(new JobOutput(stem, path));
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

    // ── Preset parsing ───────────────────────────────────────────────────────

    private sealed class PresetEntryDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("models")]
        public List<string> Models { get; set; } = [];

        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; set; }

        [JsonPropertyName("weights")]
        public List<double>? Weights { get; set; }
    }

    private static readonly JsonSerializerOptions _presetJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static IReadOnlyList<Preset> ParsePresetsFromJson(JsonElement presetsEl)
    {
        Dictionary<string, PresetEntryDto>? dict;
        try
        {
            dict = presetsEl.Deserialize<Dictionary<string, PresetEntryDto>>(_presetJsonOpts);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("driver", $"Failed to parse preset catalog: {ex.Message}");
            return [];
        }

        if (dict is null)
            return [];

        var result = new List<Preset>(dict.Count);
        foreach (var (id, entry) in dict)
        {
            var category = InferPresetCategory(id);
            var label = StripCategoryPrefix(entry.Name.Length > 0 ? entry.Name : id, category);
            result.Add(
                new Preset(
                    id,
                    label,
                    category,
                    entry.Description,
                    ModelCount: entry.Models.Count,
                    Vram: string.Empty,
                    Models: entry.Models,
                    EnsembleAlgorithm: string.IsNullOrWhiteSpace(entry.Algorithm)
                        ? null
                        : entry.Algorithm
                )
            );
        }
        return result;
    }

    private static PresetCategory InferPresetCategory(string id) =>
        id.StartsWith("vocal_") ? PresetCategory.Vocals
        : id.StartsWith("instrumental_") || id == "karaoke" ? PresetCategory.Instrumentals
        : PresetCategory.Other;

    private static string StripCategoryPrefix(string name, PresetCategory category)
    {
        var prefix = category switch
        {
            PresetCategory.Vocals => "Vocal ",
            PresetCategory.Instrumentals => "Instrumental ",
            _ => "",
        };
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name;
    }

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

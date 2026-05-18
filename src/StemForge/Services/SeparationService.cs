using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StemForge.Models;

namespace StemForge.Services;

public sealed class SeparationService(AppPaths paths)
{
    private readonly AppPaths _paths = paths;

    public async Task<IReadOnlyList<string>> RunAsync(
        string inputFile,
        IReadOnlyList<Preset> presets,
        string outputDir,
        string modelsDir,
        AudioFormat stemFormat = AudioFormat.Flac,
        IProgress<SeparationProgress>? progress = null,
        IProgress<string>? logProgress = null,
        CancellationToken ct = default
    )
    {
        Directory.CreateDirectory(outputDir);

        var jobJson = BuildJobJson(inputFile, presets, outputDir, modelsDir, stemFormat);

        var startInfo = new ProcessStartInfo(
            _paths.SeparationDriverPython,
            [AppPaths.SeparationDriverScript]
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        AppLogger.Debug("driver", $"python {AppPaths.SeparationDriverScript}");

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start separation driver");

        using var killReg = ct.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
        });

        // Write job JSON to stdin then close so the driver reads to EOF.
        await process.StandardInput.WriteAsync(jobJson);
        process.StandardInput.Close();

        var outputFiles = new List<string>();
        var presetMap = presets.ToDictionary(p => p.Id);

        // Read stdout (JSON events) and stderr (log noise) concurrently.
        var stdoutTask = Task.Run(
            async () =>
            {
                var currentLabel = string.Empty;

                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        var evt = JsonSerializer.Deserialize<JsonElement>(line);
                        if (!evt.TryGetProperty("event", out var evtProp))
                            continue;

                        switch (evtProp.GetString())
                        {
                            case "init":
                            {
                                var ver = evt.GetProperty("version").GetString() ?? "?";
                                var n = evt.GetProperty("preset_count").GetInt32();
                                AppLogger.Info("separator", $"audio-separator {ver}, {n} preset(s)");
                                break;
                            }

                            case "model_status":
                            {
                                var model = evt.GetProperty("model").GetString() ?? "";
                                var cached = evt.GetProperty("cached").GetBoolean();
                                AppLogger.Info(
                                    "separator",
                                    $"Model {model}: {(cached ? "cached" : "needs download")}"
                                );
                                break;
                            }

                            case "preset_start":
                            {
                                var idx = evt.GetProperty("index").GetInt32();
                                var total = evt.GetProperty("total").GetInt32();
                                var id = evt.GetProperty("preset_id").GetString() ?? "";
                                currentLabel =
                                    presetMap.TryGetValue(id, out var p) ? p.Label : id;
                                progress?.Report(
                                    new SeparationProgress(idx, total, currentLabel, "Loading model", 0)
                                );
                                break;
                            }

                            case "preset_progress":
                            {
                                var idx = evt.GetProperty("index").GetInt32();
                                var total = evt.GetProperty("total").GetInt32();
                                var pct = evt.GetProperty("pct").GetInt32();
                                progress?.Report(
                                    new SeparationProgress(idx, total, currentLabel, "Separating", pct)
                                );
                                break;
                            }

                            case "preset_done":
                            {
                                var idx = evt.GetProperty("index").GetInt32();
                                var total = evt.GetProperty("total").GetInt32();
                                var id = evt.GetProperty("preset_id").GetString() ?? "";
                                var label = presetMap.TryGetValue(id, out var p) ? p.Label : id;
                                progress?.Report(
                                    new SeparationProgress(idx, total, label, "Done", 100)
                                );

                                if (evt.TryGetProperty("files", out var filesEl))
                                {
                                    foreach (var f in filesEl.EnumerateArray())
                                    {
                                        var fname = f.GetString();
                                        if (string.IsNullOrWhiteSpace(fname))
                                            continue;
                                        var full = Path.IsPathRooted(fname)
                                            ? fname
                                            : Path.Combine(outputDir, fname);
                                        if (File.Exists(full))
                                            outputFiles.Add(full);
                                    }
                                }
                                break;
                            }

                            case "preset_error":
                            {
                                var id = evt.GetProperty("preset_id").GetString() ?? "";
                                var msg = evt.GetProperty("message").GetString() ?? "unknown error";
                                AppLogger.Error("separator", $"Preset '{id}' failed: {msg}");
                                logProgress?.Report($"[Error] {id}: {msg}");
                                break;
                            }

                            case "job_error":
                            {
                                var msg =
                                    evt.GetProperty("message").GetString() ?? "unknown error";
                                throw new InvalidOperationException(
                                    $"Separation job failed: {msg}"
                                );
                            }

                            case "job_done":
                                AppLogger.Info("separator", "Job complete");
                                break;
                        }
                    }
                    catch (JsonException)
                    {
                        AppLogger.Warning("driver", $"Unreadable event: {line}");
                    }
                }
            },
            CancellationToken.None
        );

        var stderrTask = Task.Run(
            async () =>
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                {
                    var cleaned = CleanStderrLine(line);
                    if (cleaned is null)
                        continue;
                    AppLogger.Info("separator", cleaned);
                    logProgress?.Report(cleaned);
                }
            },
            CancellationToken.None
        );

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Separation driver exited with code {process.ExitCode}"
            );

        return outputFiles;
    }

    // ── Job JSON ──────────────────────────────────────────────────────────────

    private static string BuildJobJson(
        string inputFile,
        IReadOnlyList<Preset> presets,
        string outputDir,
        string modelsDir,
        AudioFormat stemFormat
    )
    {
        var job = new
        {
            input_file = inputFile,
            output_dir = outputDir,
            model_dir = modelsDir,
            stem_format = FfmpegArgs.Extension(stemFormat).ToUpperInvariant(),
            presets = presets.Select(BuildPresetDto).ToArray(),
        };
        return JsonSerializer.Serialize(job);
    }

    private static object BuildPresetDto(Preset preset) =>
        preset.Mode switch
        {
            SeparationMode.SingleModel => new
            {
                id = preset.Id,
                category = preset.Category.ToString(),
                models = new[]
                {
                    preset.PrimaryModel
                        ?? throw new InvalidOperationException(
                            $"Preset '{preset.Id}' is SingleModel but has no PrimaryModel."
                        ),
                },
            },

            SeparationMode.CustomEnsemble => (object)
                new
                {
                    id = preset.Id,
                    category = preset.Category.ToString(),
                    models = new[]
                    {
                        preset.PrimaryModel
                            ?? throw new InvalidOperationException(
                                $"Preset '{preset.Id}' is CustomEnsemble but has no PrimaryModel."
                            ),
                    }
                        .Concat(preset.ExtraModels ?? [])
                        .ToArray(),
                    ensemble_algorithm = preset.EnsembleAlgorithm ?? "avg_wave",
                },

            // BuiltinPreset (default): driver passes the ID as the ensemble preset name.
            _ => (object)
                new
                {
                    id = preset.Id,
                    category = preset.Category.ToString(),
                    ensemble_preset = preset.PrimaryModel ?? preset.Id,
                },
        };

    // ── Stderr cleanup ────────────────────────────────────────────────────────

    // Driver formats stderr as "{logger_name} - {message}". Strip the prefix.
    private static string? CleanStderrLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim();
        var dashIdx = s.IndexOf(" - ", StringComparison.Ordinal);
        return dashIdx > 0 ? s[(dashIdx + 3)..].TrimStart() : s;
    }
}

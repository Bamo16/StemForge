using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using StemForge.Extensions;
using StemForge.Models;

namespace StemForge.Services;

public sealed partial class SeparationService(string audioSeparatorPath = "audio-separator")
{
    private readonly string _audioSeparatorPath = audioSeparatorPath;

    /// <summary>Matches any tqdm progress bar — captures the leading percentage.</summary>
    [GeneratedRegex(@"^\s*(\d+)%\|")]
    private static partial Regex TqdmPctRegex();

    public static string ResolveModelsDir() =>
        Environment.GetEnvironmentVariable("AUDIO_SEPARATOR_MODEL_DIR") is { Length: > 0 } envDir
            ? envDir
            : Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
                "audio-separator",
                "models"
            );

    public async Task RunAsync(
        string inputFile,
        IReadOnlyList<Preset> presets,
        string outputDir,
        string modelsDir,
        IProgress<SeparationProgress>? progress = null,
        IProgress<string>? logProgress = null,
        CancellationToken ct = default
    )
    {
        Directory.CreateDirectory(outputDir);
        var baseName = Path.GetFileNameWithoutExtension(inputFile);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"stemforge-{Guid.NewGuid():N}");
        var total = presets.Count;

        try
        {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var preset = presets[i];
                var passDir = Path.Combine(tempRoot, preset.Id);
                Directory.CreateDirectory(passDir);

                progress?.Report(new SeparationProgress(i, total, preset.Label, preset.Label, 0));

                await RunPassAsync(
                    inputFile,
                    preset,
                    modelsDir,
                    passDir,
                    i,
                    total,
                    progress,
                    logProgress,
                    ct
                );

                if (preset.Mode == SeparationMode.BuiltinPreset)
                {
                    // Built-in presets produce exactly one relevant stem; pick it by category.
                    var stemFile =
                        FindStem(passDir, preset.Category)
                        ?? throw new FileNotFoundException(
                            $"audio-separator produced no output in '{passDir}' for preset '{preset.Id}'"
                        );
                    var outPath = Path.Combine(
                        outputDir,
                        $"{baseName} ({preset.Category} - {preset.Label}).flac"
                    );
                    File.Copy(stemFile, outPath, overwrite: true);
                }
                else
                {
                    // Single-model and custom ensemble: copy every stem produced.
                    foreach (var f in Directory.GetFiles(passDir, "*.flac"))
                        File.Copy(f, Path.Combine(outputDir, Path.GetFileName(f)), overwrite: true);
                }
            }

            progress?.Report(new SeparationProgress(total - 1, total, string.Empty, "Done", 100));
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch { }
        }
    }

    private async Task RunPassAsync(
        string inputFile,
        Preset preset,
        string modelsDir,
        string passDir,
        int passIndex,
        int totalPasses,
        IProgress<SeparationProgress>? progress,
        IProgress<string>? logProgress,
        CancellationToken ct
    )
    {
        var args = BuildArgs(inputFile, preset, modelsDir, passDir);

        var startInfo = new ProcessStartInfo(_audioSeparatorPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{_audioSeparatorPath}'");

        using var killReg = ct.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
        });

        var queue = new BlockingCollection<string>();

        var outTask = Task.Run(
            () =>
            {
                while (process.StandardOutput.ReadLine() is { } line)
                    queue.Add(line);
            },
            CancellationToken.None
        );
        var errTask = Task.Run(
            () =>
            {
                while (process.StandardError.ReadLine() is { } line)
                    queue.Add(line);
            },
            CancellationToken.None
        );
        _ = Task.Run(
            async () =>
            {
                await Task.WhenAll(outTask, errTask);
                queue.CompleteAdding();
            },
            CancellationToken.None
        );

        await Task.Run(
            () =>
            {
                var stepLabel = preset.Label;

                foreach (var line in queue.GetConsumingEnumerable(ct))
                {
                    var match = TqdmPctRegex().Match(line);
                    if (match.Success)
                    {
                        var tqdmPct = int.Parse(match.Groups[1].Value);
                        progress?.Report(
                            new SeparationProgress(
                                passIndex,
                                totalPasses,
                                preset.Label,
                                stepLabel,
                                tqdmPct
                            )
                        );
                    }
                    else
                    {
                        var newStep = ParseStepLabel(line);
                        if (newStep is not null)
                        {
                            stepLabel = newStep;
                            progress?.Report(
                                new SeparationProgress(
                                    passIndex,
                                    totalPasses,
                                    preset.Label,
                                    stepLabel,
                                    0
                                )
                            );
                        }

                        var cleaned = CleanLogLine(line);
                        if (cleaned is not null)
                        {
                            AppLogger.Info("audio-separator", cleaned);
                            logProgress?.Report(cleaned);
                        }
                    }
                }
            },
            ct
        );

        await process.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"audio-separator exited with code {process.ExitCode} on preset '{preset.Id}'"
            );
    }

    internal static IEnumerable<string> BuildArgs(
        string inputFile,
        Preset preset,
        string modelsDir,
        string passDir
    )
    {
        // Common tail args shared by all modes.
        IEnumerable<string> Tail() =>
            ["--model_file_dir", modelsDir, "--output_dir", passDir, "--output_format", "FLAC"];

        return preset.Mode switch
        {
            SeparationMode.SingleModel =>
            [
                inputFile,
                "--model_filename",
                preset.PrimaryModel
                    ?? throw new InvalidOperationException(
                        $"Preset '{preset.Id}' is SingleModel but has no PrimaryModel set."
                    ),
                .. Tail(),
            ],

            SeparationMode.CustomEnsemble => BuildCustomEnsembleArgs(inputFile, preset, Tail()),

            // BuiltinPreset (default)
            _ => [inputFile, "--ensemble_preset", preset.PrimaryModel ?? preset.Id, .. Tail()],
        };
    }

    internal static IEnumerable<string> BuildCustomEnsembleArgs(
        string inputFile,
        Preset preset,
        IEnumerable<string> tail
    )
    {
        var primary =
            preset.PrimaryModel
            ?? throw new InvalidOperationException(
                $"Preset '{preset.Id}' is CustomEnsemble but has no PrimaryModel set."
            );

        var args = new List<string> { inputFile, "--model_filename", primary };

        if (preset.ExtraModels is { Count: > 0 })
        {
            args.Add("--extra_models");
            args.AddRange(preset.ExtraModels);
        }

        if (!string.IsNullOrWhiteSpace(preset.EnsembleAlgorithm))
        {
            args.Add("--ensemble_algorithm");
            args.Add(preset.EnsembleAlgorithm);
        }

        if (preset.EnsembleWeights is { Count: > 0 })
        {
            args.Add("--ensemble_weights");
            args.AddRange(preset.EnsembleWeights.Select(w => w.ToString("G")));
        }

        args.AddRange(tail);
        return args;
    }

    // Detect a step change from a non-tqdm log line and return a short label, or null.
    internal static string? ParseStepLabel(string line)
    {
        // audio-separator emits either "LEVEL:module:message" or "timestamp - LEVEL - module - message"
        var msg = line;
        var dashIdx = line.IndexOf(" - INFO - ", StringComparison.Ordinal);
        if (dashIdx >= 0)
        {
            var msgStart = line.IndexOf(" - ", dashIdx + 10, StringComparison.Ordinal);
            msg = msgStart >= 0 ? line[(msgStart + 3)..] : line[(dashIdx + 10)..];
        }

        if (msg.Contains("Starting separation", StringComparison.OrdinalIgnoreCase))
            return "Separating";
        if (msg.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            return "Downloading model";
        if (
            msg.Contains("Processing with model:", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Loading model", StringComparison.OrdinalIgnoreCase)
        )
            return "Loading model";
        if (
            msg.Contains("ensemble", StringComparison.OrdinalIgnoreCase)
            && (
                msg.Contains("processing", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("creating", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("running", StringComparison.OrdinalIgnoreCase)
            )
        )
            return "Creating ensemble";
        return null;
    }

    // Strip Python logging prefix from a log line for display.
    internal static string? CleanLogLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim();

        // "timestamp - LEVEL - module - message"
        var dashIdx = s.IndexOf(" - INFO - ", StringComparison.Ordinal);
        if (dashIdx < 0)
            dashIdx = s.IndexOf(" - WARNING - ", StringComparison.Ordinal);
        if (dashIdx < 0)
            dashIdx = s.IndexOf(" - ERROR - ", StringComparison.Ordinal);
        if (dashIdx >= 0)
        {
            var msgStart = s.IndexOf(" - ", dashIdx + 4, StringComparison.Ordinal);
            if (msgStart >= 0)
                s = s[(msgStart + 3)..].TrimStart();
        }
        else
        {
            // "INFO:some.module:message"
            var first = s.IndexOf(':', StringComparison.Ordinal);
            if (first > 0)
            {
                var second = s.IndexOf(':', first + 1);
                if (second > 0)
                    s = s[(second + 1)..].TrimStart();
            }
        }

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    internal static string? FindStem(string passDir, PresetCategory category)
    {
        var keyword = category switch
        {
            PresetCategory.Vocals => "(Vocals)",
            PresetCategory.Instrumentals => "(Instrumental)",
            PresetCategory.Drums => "(Drums)",
            PresetCategory.Bass => "(Bass)",
            PresetCategory.Guitar => "(Guitar)",
            PresetCategory.Piano => "(Piano)",
            PresetCategory.Other => "(Other)",
            _ => null,
        };
        if (keyword is null)
            return null;
        return Directory
            .GetFiles(passDir, "*.flac")
            .FirstOrDefault(f =>
                Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase)
            );
    }
}

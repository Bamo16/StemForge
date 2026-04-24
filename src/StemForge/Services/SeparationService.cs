using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using StemForge.Models;

namespace StemForge.Services;

public sealed partial class SeparationService
{
    private readonly string _audioSeparatorPath;

    public SeparationService(string audioSeparatorPath = "audio-separator")
    {
        _audioSeparatorPath = audioSeparatorPath;
    }

    // Matches tqdm chunk progress: "  57%|######    | 18/32 [00:31<00:24, 1.75s/it]"
    [GeneratedRegex(@"(\d+)%\|[^\|]*\|\s*\d+/\d+")]
    private static partial Regex TqdmChunkRegex();

    // Matches tqdm percentage with no chunk count (download progress, model loading)
    [GeneratedRegex(@"(\d+)%\|")]
    private static partial Regex TqdmPctRegex();

    public static string ResolveModelsDir()
    {
        var envDir = Environment.GetEnvironmentVariable("AUDIO_SEPARATOR_MODEL_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return envDir;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "audio-separator",
            "models"
        );
    }

    public async Task RunAsync(
        string inputFile,
        IReadOnlyList<Preset> presets,
        string outputDir,
        string modelsDir,
        IProgress<SeparationProgress>? progress = null,
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

                progress?.Report(new SeparationProgress(i * 100 / total, $"{preset.Label} ({i + 1}/{total})", null));

                await RunPassAsync(inputFile, preset, modelsDir, passDir, i, total, progress, ct);

                var stemFile = FindStem(passDir, preset.Category);
                if (stemFile is null)
                    throw new FileNotFoundException(
                        $"audio-separator produced no output in '{passDir}' for preset '{preset.Id}'"
                    );

                var outPath = Path.Combine(outputDir, $"{baseName} ({preset.Id}).flac");
                File.Copy(stemFile, outPath, overwrite: true);
            }

            progress?.Report(new SeparationProgress(100, "Done", null));
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
        CancellationToken ct
    )
    {
        string[] args =
        [
            inputFile,
            "--ensemble_preset",
            preset.Id,
            "--model_file_dir",
            modelsDir,
            "--output_dir",
            passDir,
            "--output_format",
            "FLAC",
        ];

        var startInfo = new ProcessStartInfo(_audioSeparatorPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

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

        var passStartPct = passIndex * 100 / totalPasses;
        var passWeight = 100 / totalPasses;
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
                foreach (var line in queue.GetConsumingEnumerable(ct))
                {
                    var match = TqdmChunkRegex().Match(line);
                    if (!match.Success)
                        match = TqdmPctRegex().Match(line);
                    if (!match.Success)
                        continue;

                    var tqdmPct = int.Parse(match.Groups[1].Value);
                    var overallPct = passStartPct + tqdmPct * passWeight / 100;
                    progress?.Report(new SeparationProgress(overallPct, preset.Label, tqdmPct));
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

    private static string? FindStem(string passDir, PresetCategory category) =>
        Directory
            .GetFiles(passDir, "*.flac")
            .FirstOrDefault(f =>
                category == PresetCategory.Vocals
                    ? Path.GetFileName(f).Contains("(Vocals)", StringComparison.OrdinalIgnoreCase)
                    : Path.GetFileName(f)
                            .Contains("(Instrumental)", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(f)
                            .Contains("(Other)", StringComparison.OrdinalIgnoreCase)
            );
}

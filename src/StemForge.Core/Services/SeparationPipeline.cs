using StemForge.Core.Helpers;
using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// UI-agnostic separation orchestration. Runs the full pipeline for a single
/// <see cref="JobRecord"/> — download, all preset runs, optional drum extraction,
/// keep-source — and reports progress via a single <see cref="IProgress{JobUpdate}"/> stream.
///
/// Toolchain failures propagate as exceptions; the caller is responsible for mapping
/// <see cref="OperationCanceledException"/> and <see cref="Exception"/> to terminal state.
/// </summary>
public sealed class SeparationPipeline(
    ISeparatorDriverService driver,
    YouTubeAudioService youTubeAudio,
    IThumbnailFetcher thumbnailFetcher,
    IProcessRunner runner,
    AppSettings settings,
    AppPaths paths,
    IAppInfo appInfo
)
{
    private readonly ISeparatorDriverService _driver = driver;
    private readonly YouTubeAudioService _youTubeAudio = youTubeAudio;
    private readonly IThumbnailFetcher _thumbnailFetcher = thumbnailFetcher;
    private readonly IProcessRunner _runner = runner;
    private readonly AppSettings _settings = settings;
    private readonly AppPaths _paths = paths;
    private readonly IAppInfo _appInfo = appInfo;

    /// <summary>
    /// Runs all pipeline stages for <paramref name="job"/>. Progress updates are reported via
    /// <paramref name="progress"/>. Cancellation propagates as <see cref="OperationCanceledException"/>;
    /// toolchain failures propagate as <see cref="InvalidOperationException"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        JobRecord job,
        IProgress<JobUpdate>? progress,
        CancellationToken ct
    )
    {
        var allOutputFiles = new List<string>();
        SourceTagInfo? sourceInfo = null;
        var version = _appInfo.FullVersion;

        // ── Download step ─────────────────────────────────────────────────────
        string inputFile;
        if (job.SourceUrl is { Length: > 0 } url)
        {
            progress?.Report(
                new JobUpdate
                {
                    Phase = "downloading",
                    OverallPercent = 0,
                    RunIndex = 0,
                    RunCount = 0,
                }
            );

            var dlLog = new Progress<string>(_ => { }); // log lines go to AppLogger inside YouTubeAudioService

            var dlTempDir = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dlTempDir);

            try
            {
                (inputFile, sourceInfo) = await DownloadAudioAsync(
                    url,
                    dlTempDir,
                    dlLog,
                    ct,
                    job.PreResolvedMeta
                );
            }
            catch
            {
                // Clean up on download failure before propagating.
                try
                {
                    Directory.Delete(dlTempDir, recursive: true);
                }
                catch { }
                throw;
            }
        }
        else
        {
            inputFile =
                job.InputFilePath
                ?? throw new InvalidOperationException(
                    "Job has neither a file path nor a source URL."
                );
            sourceInfo = AudioTagger.ReadFromFile(inputFile);
        }

        // ── Separation step — one driver run per preset ───────────────────────
        var presets = job.Presets;
        var format = FfmpegArgs.Extension(job.StemOutputFormat).ToUpperInvariant();
        var totalSteps = presets.Count + (job.ExtractDrums ? 1 : 0);

        // Weight each run by its model count so multi-model presets occupy proportionally
        // more of the progress bar. Guard against a degenerate zero-weight total.
        var totalModelWeight = Math.Max(
            1,
            presets.Sum(p => p.ModelCount) + (job.ExtractDrums ? 1 : 0)
        );
        int cumulativeModelWeight = 0;

        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            var runLabel = EffectiveLabel(preset);

            // Capture loop-local values for use inside the lambda.
            int runIndex = i;
            int runStartWeight = cumulativeModelWeight;

            progress?.Report(
                new JobUpdate
                {
                    Phase = "starting",
                    RunIndex = runIndex,
                    RunCount = totalSteps,
                    RunLabel = runLabel,
                    OverallPercent = (int)Math.Round(runStartWeight * 100.0 / totalModelWeight),
                }
            );

            // Per-run model tracking for within-preset percentage math.
            int currentModelIndex = 1;
            int currentModelCount = 1;

            var driverProgress = new Progress<JobProgress>(p =>
            {
                var phase = p as PhaseProgress;
                var tick = p as ProgressTick;
                var log = p as LogLine;
                var stem = p as StemWritten;

                if (phase is { Phase: JobPhase.LoadingModel })
                {
                    currentModelIndex = phase.ModelIndex ?? 1;
                    currentModelCount = phase.ModelCount ?? 1;
                }

                int overallPercent;
                if (tick is { Total: > 0 and var total, Current: { } cur })
                {
                    var withinModel = Math.Min(100, cur * 100 / total);
                    var withinPreset =
                        ((currentModelIndex - 1) * 100 + withinModel) / currentModelCount;
                    // Cap at 99: some models (e.g. Demucs BagOfModels) reset the progress bar
                    // multiple times internally, so a 100% event does not mean the run is done.
                    // The pipeline snaps to 100% via run_complete once the driver call returns.
                    overallPercent = Math.Min(
                        99,
                        (int)
                            Math.Round(
                                (runStartWeight + withinPreset * preset.ModelCount / 100.0)
                                    * 100.0
                                    / totalModelWeight
                            )
                    );
                }
                else
                {
                    overallPercent = (int)Math.Round(runStartWeight * 100.0 / totalModelWeight);
                }

                progress?.Report(
                    new JobUpdate
                    {
                        Phase = p.UpdatePhase,
                        RunIndex = runIndex,
                        RunCount = totalSteps,
                        RunLabel = runLabel,
                        Model = phase?.Model,
                        ModelIndex = phase?.ModelIndex,
                        ModelCount = phase?.ModelCount,
                        Cached = phase?.Cached,
                        Stem = phase?.Stem,
                        ProgressCurrent = tick?.Current,
                        ProgressTotal = tick?.Total,
                        ProgressFinal = tick?.Final,
                        OutputPath = stem?.Path,
                        LogMessage = log?.Message,
                        LogLevel = log?.Level,
                        OverallPercent = overallPercent,
                    }
                );
            });

            var request = BuildRequest(preset, inputFile, job.OutputDir, format);
            var result = await _driver.RunAsync(request, driverProgress, ct);

            if (!result.Succeeded)
                throw new InvalidOperationException(result.ErrorMessage ?? "Separation failed");

            var runPaths = new List<string>();
            foreach (var o in result.Outputs)
            {
                runPaths.Add(o.Path);
                allOutputFiles.Add(o.Path);
                AudioTagger.ApplyToFile(o.Path, sourceInfo, preset.DisplayName, version);
            }

            cumulativeModelWeight += preset.ModelCount;

            progress?.Report(
                new JobUpdate
                {
                    Phase = "run_complete",
                    RunIndex = runIndex,
                    RunCount = totalSteps,
                    RunLabel = runLabel,
                    WrittenPaths = runPaths,
                    OverallPercent = (int)
                        Math.Round(cumulativeModelWeight * 100.0 / totalModelWeight),
                }
            );
        }

        // ── Drum extraction step ──────────────────────────────────────────────
        if (job.ExtractDrums)
        {
            int drumIndex = presets.Count;
            const string drumLabel = "Drums";
            // cumulativeModelWeight now equals the sum of all preset model counts.
            int drumStartWeight = cumulativeModelWeight;

            progress?.Report(
                new JobUpdate
                {
                    Phase = "starting",
                    RunIndex = drumIndex,
                    RunCount = totalSteps,
                    RunLabel = drumLabel,
                    OverallPercent = (int)Math.Round(drumStartWeight * 100.0 / totalModelWeight),
                }
            );

            var drumOutDir =
                _settings.DrumStemLocation == DrumStemLocation.WithStems
                    ? job.OutputDir
                    : _paths.DrumCacheDirectory;

            Directory.CreateDirectory(drumOutDir);

            int drumModelIndex = 1;
            int drumModelCount = 1;

            var drumProgress = new Progress<JobProgress>(p =>
            {
                var phase = p as PhaseProgress;
                var tick = p as ProgressTick;
                var log = p as LogLine;
                var stem = p as StemWritten;

                if (phase is { Phase: JobPhase.LoadingModel })
                {
                    drumModelIndex = phase.ModelIndex ?? 1;
                    drumModelCount = phase.ModelCount ?? 1;
                }

                int overallPercent;
                if (tick is { Total: > 0 and var total, Current: { } cur })
                {
                    var withinModel = Math.Min(100, cur * 100 / total);
                    var withinPreset = ((drumModelIndex - 1) * 100 + withinModel) / drumModelCount;
                    // Cap at 99 for the same reason as the preset loop (Demucs BagOfModels).
                    overallPercent = Math.Min(
                        99,
                        (int)
                            Math.Round(
                                (drumStartWeight + withinPreset / 100.0) * 100.0 / totalModelWeight
                            )
                    );
                }
                else
                {
                    overallPercent = (int)Math.Round(drumStartWeight * 100.0 / totalModelWeight);
                }

                progress?.Report(
                    new JobUpdate
                    {
                        Phase = p.UpdatePhase,
                        RunIndex = drumIndex,
                        RunCount = totalSteps,
                        RunLabel = drumLabel,
                        Model = phase?.Model,
                        ModelIndex = phase?.ModelIndex,
                        ModelCount = phase?.ModelCount,
                        Cached = phase?.Cached,
                        Stem = phase?.Stem,
                        ProgressCurrent = tick?.Current,
                        ProgressTotal = tick?.Total,
                        ProgressFinal = tick?.Final,
                        OutputPath = stem?.Path,
                        LogMessage = log?.Message,
                        LogLevel = log?.Level,
                        OverallPercent = overallPercent,
                    }
                );
            });

            var drumTitle = Path.GetFileNameWithoutExtension(inputFile);
            var drumRequest = new JobRequest(
                inputFile,
                drumOutDir,
                format,
                PresetId: null,
                Models: [_settings.DrumExtractionModel],
                Algorithm: null,
                StemsToKeep: ["Drums"]
            );

            var drumResult = await _driver.RunAsync(drumRequest, drumProgress, ct);

            if (drumResult.Succeeded)
            {
                var drumStem = drumResult.Outputs.FirstOrDefault(o =>
                    o.Stem.Equals("Drums", StringComparison.OrdinalIgnoreCase)
                );

                // Delete all non-Drums files (htdemucs writes all 4 stems regardless of StemsToKeep).
                foreach (
                    var o in drumResult
                        .Outputs.Concat(drumResult.Discarded)
                        .Where(o => !o.Stem.Equals("Drums", StringComparison.OrdinalIgnoreCase))
                )
                    try
                    {
                        if (File.Exists(o.Path))
                            File.Delete(o.Path);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning(
                            "job",
                            $"Could not delete unwanted drum stem {Path.GetFileName(o.Path)}: {ex.Message}"
                        );
                    }

                if (drumStem is not null)
                {
                    var drumExt = Path.GetExtension(drumStem.Path).ToLowerInvariant();
                    var renamedPath = Path.Combine(drumOutDir, $"{drumTitle} (Drums){drumExt}");
                    if (
                        !string.Equals(
                            drumStem.Path,
                            renamedPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        File.Move(drumStem.Path, renamedPath, overwrite: true);

                    var drumPreset = Preset.DrumExtraction(_settings.DrumExtractionModel);
                    AudioTagger.ApplyToFile(
                        renamedPath,
                        sourceInfo,
                        drumPreset.DisplayName,
                        version
                    );
                    if (_settings.DrumStemLocation == DrumStemLocation.WithStems)
                        allOutputFiles.Add(renamedPath);
                }
            }
            else
            {
                AppLogger.Warning("job", $"Drum extraction failed: {drumResult.ErrorMessage}");
            }

            progress?.Report(
                new JobUpdate
                {
                    Phase = "run_complete",
                    RunIndex = drumIndex,
                    RunCount = totalSteps,
                    RunLabel = drumLabel,
                    OverallPercent = 100,
                }
            );
        }

        // ── Keep-source step ──────────────────────────────────────────────────
        if (job.KeepSourceFile && job.SourceUrl is not null)
        {
            progress?.Report(new JobUpdate { Phase = "keep_source", OverallPercent = 100 });

            var keptSource = await KeepSourceFileAsync(
                inputFile,
                job.StemOutputFormat,
                job.OutputDir,
                ct
            );
            if (keptSource is not null)
            {
                AudioTagger.ApplyToFile(keptSource, sourceInfo, presetDescriptor: null, version);
                allOutputFiles.Add(keptSource);
            }
        }

        return allOutputFiles;
    }

    /// <summary>
    /// Downloads the audio for a URL-sourced <paramref name="job"/> into its output directory
    /// in the requested format, applies metadata, provenance, and thumbnail tags, and returns
    /// the written file path. No separation is performed.
    ///
    /// Cancellation propagates as <see cref="OperationCanceledException"/>; download failures
    /// propagate as <see cref="InvalidOperationException"/> (or the underlying toolchain exception).
    /// </summary>
    public async Task<string> DownloadOnlyAsync(
        JobRecord job,
        IProgress<JobUpdate>? progress,
        CancellationToken ct
    )
    {
        var url = job.SourceUrl is { Length: > 0 } u
            ? u
            : throw new InvalidOperationException(
                "DownloadOnlyAsync requires a job with a source URL."
            );

        var version = _appInfo.FullVersion;

        progress?.Report(
            new JobUpdate
            {
                Phase = "downloading",
                OverallPercent = 0,
                RunIndex = 0,
                RunCount = 0,
            }
        );

        var log = new Progress<string>(_ => { }); // log lines go to AppLogger inside YouTubeAudioService

        // Resolve metadata + thumbnail, then stream directly to the output directory in the
        // requested format. Unlike the separation pipeline, there is no intermediate FLAC: the
        // download IS the deliverable, so it is written once in its final format.
        var meta =
            job.PreResolvedMeta
            ?? await _youTubeAudio.ResolveAsync(
                YtUrlHelper.TryNormalize(url, out var normalized) ? normalized : url,
                _settings,
                log,
                ct
            );

        // The thumbnail is fetched to a temp directory and embedded as cover art; the standalone
        // image file is not part of the deliverable, so it is removed after tagging rather than
        // left beside the audio in the output directory.
        var thumbTempDir = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(thumbTempDir);

        string audioPath;
        SourceTagInfo sourceInfo;
        try
        {
            var thumbPath = await _thumbnailFetcher.DownloadAsync(
                meta.ThumbnailUrl,
                thumbTempDir,
                ct
            );
            sourceInfo = AudioTagger.FromYtDlpMetadata(meta, thumbPath);

            audioPath = await _youTubeAudio.DownloadAsync(
                meta,
                job.StemOutputFormat,
                job.OutputDir,
                log,
                ct
            );
        }
        finally
        {
            try
            {
                Directory.Delete(thumbTempDir, recursive: true);
            }
            catch { }
        }

        // No preset descriptor: this is a verbatim source download, not a separation output.
        AudioTagger.ApplyToFile(audioPath, sourceInfo, presetDescriptor: null, version);

        progress?.Report(
            new JobUpdate
            {
                Phase = "run_complete",
                OverallPercent = 100,
                RunIndex = 0,
                RunCount = 0,
                WrittenPaths = [audioPath],
            }
        );

        return audioPath;
    }

    // ── Request builder ───────────────────────────────────────────────────────

    internal static JobRequest BuildRequest(
        Preset preset,
        string audioPath,
        string outputDir,
        string format
    ) =>
        preset.Mode switch
        {
            SeparationMode.SingleModel => new JobRequest(
                audioPath,
                outputDir,
                format,
                PresetId: null,
                Models:
                [
                    preset.PrimaryModel
                        ?? throw new InvalidOperationException(
                            $"Preset '{preset.Id}' has no PrimaryModel"
                        ),
                ],
                Algorithm: null
            ),

            SeparationMode.CustomEnsemble => new JobRequest(
                audioPath,
                outputDir,
                format,
                PresetId: null,
                Models:
                [
                    preset.PrimaryModel
                        ?? throw new InvalidOperationException(
                            $"Preset '{preset.Id}' has no PrimaryModel"
                        ),
                    .. preset.ExtraModels ?? [],
                ],
                Algorithm: preset.EnsembleAlgorithm ?? "avg_wave"
            ),

            _ => new JobRequest( // BuiltinPreset
                audioPath,
                outputDir,
                format,
                PresetId: preset.Id,
                Models: null,
                Algorithm: null,
                CustomOutputNames: BuildPresetOutputNames(preset, audioPath)
            ),
        };

    internal static Dictionary<string, string> BuildPresetOutputNames(
        Preset preset,
        string audioPath
    )
    {
        var title = Path.GetFileNameWithoutExtension(audioPath);
        var stemKey = preset.Category == PresetCategory.Vocals ? "Vocals" : "Instrumental";
        var label =
            preset.Id == "karaoke"
                ? "Karaoke"
                : $"{(preset.Category == PresetCategory.Vocals ? "Vocal" : "Instrumental")} - {SanitizeLabel(preset.Label)}";
        return new Dictionary<string, string> { [stemKey] = $"{title} ({label})" };
    }

    internal static string EffectiveLabel(Preset preset) =>
        preset.Mode == SeparationMode.BuiltinPreset
            ? $"{CategoryName(preset.Category)} {preset.Label}"
            : preset.Label;

    internal static string CategoryName(PresetCategory category) =>
        category switch
        {
            PresetCategory.Vocals => "Vocals",
            PresetCategory.Instrumentals => "Instrumental",
            PresetCategory.Drums => "Drums",
            PresetCategory.Bass => "Bass",
            PresetCategory.Guitar => "Guitar",
            PresetCategory.Piano => "Piano",
            _ => category.ToString(),
        };

    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

    internal static string SanitizeLabel(string label) =>
        string.Concat(label.Select(c => _invalidFileNameChars.Contains(c) ? '-' : c)).Trim();

    // ── Keep-source helper ────────────────────────────────────────────────────

    private async Task<string?> KeepSourceFileAsync(
        string sourceFlac,
        AudioFormat format,
        string outputDir,
        CancellationToken ct
    )
    {
        try
        {
            Directory.CreateDirectory(outputDir);
            var baseName = Path.GetFileNameWithoutExtension(sourceFlac);
            var dest = Path.Combine(outputDir, $"{baseName}.{FfmpegArgs.Extension(format)}");

            if (format == AudioFormat.Flac)
            {
                File.Copy(sourceFlac, dest, overwrite: true);
            }
            else
            {
                var args = new List<string>();
                args.AddRange(FfmpegArgs.Baseline);
                args.AddRange(["-i", sourceFlac]);
                args.AddRange(FfmpegArgs.Codec(format));
                args.Add(dest);
                await _runner.RunCheckedAsync(_paths.Ffmpeg, args, ct, logRawLines: false);
            }

            return dest;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("job", $"Failed to keep source file: {ex.Message}");
            return null;
        }
    }

    // ── Download helper ───────────────────────────────────────────────────────

    private async Task<(string AudioPath, SourceTagInfo SourceInfo)> DownloadAudioAsync(
        string url,
        string dlDir,
        IProgress<string> log,
        CancellationToken ct,
        YtDlpMetadata? preResolved = null
    )
    {
        var meta =
            preResolved
            ?? await _youTubeAudio.ResolveAsync(
                YtUrlHelper.TryNormalize(url, out var n) ? n : url,
                _settings,
                log,
                ct
            );
        var thumbPath = await _thumbnailFetcher.DownloadAsync(meta.ThumbnailUrl, dlDir, ct);
        var sourceInfo = AudioTagger.FromYtDlpMetadata(meta, thumbPath);
        var audioPath = await _youTubeAudio.DownloadAsync(meta, AudioFormat.Flac, dlDir, log, ct);
        return (audioPath, sourceInfo);
    }
}

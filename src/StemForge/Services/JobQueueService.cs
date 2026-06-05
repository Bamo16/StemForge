using System.Collections.ObjectModel;
using Avalonia.Threading;
using StemForge.Helpers;
using StemForge.Models;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Runs separation jobs sequentially. One user-submitted job at a time; all others
/// wait in FIFO order. Thread-safe enqueue; all observable mutations happen on the UI thread.
/// </summary>
public sealed class JobQueueService(
    ISeparatorDriverService driver,
    AppSettings settings,
    IProcessRunner runner,
    YouTubeAudioService youTubeAudio,
    AppPaths paths,
    IAppInfo appInfo
)
{
    private readonly ISeparatorDriverService _driver = driver;
    private readonly AppSettings _settings = settings;
    private readonly IProcessRunner _runner = runner;
    private readonly YouTubeAudioService _youTubeAudio = youTubeAudio;
    private readonly AppPaths _paths = paths;
    private readonly IAppInfo _appInfo = appInfo;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _currentCts;
    private JobItemViewModel? _currentJob;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    public int ActiveCount =>
        Jobs.Count(job => job is { Status: JobStatus.Running or JobStatus.Queued });

    public void Enqueue(JobRecord record)
    {
        var vm = new JobItemViewModel(record, _settings.MaxJobLogLines)
        {
            CancelRequested = OnCancelRequested,
        };

        Dispatcher.UIThread.Post(() => Jobs.Add(vm));

        _ = Task.Run(() => RunWhenReadyAsync(vm));
    }

    private async Task RunWhenReadyAsync(JobItemViewModel vm)
    {
        await _gate.WaitAsync();
        var cts = new CancellationTokenSource();
        _currentCts = cts;
        _currentJob = vm;

        Dispatcher.UIThread.Post(() =>
        {
            vm.Status = JobStatus.Running;
            vm.Progress = 0;
            vm.StatusText = "Starting…";
            OnPropertyChanged();
        });

        string? dlTempDir = null;
        var outputFiles = new List<string>();
        SourceTagInfo? sourceInfo = null;

        try
        {
            // ── Download step (URL input only) ─────────────────────────────────
            string inputFile;
            if (vm.Job.SourceUrl is { Length: > 0 } url)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    vm.Progress = 0;
                    vm.StatusText = "Downloading…";
                });

                var dlLog = new Progress<string>(line =>
                    Dispatcher.UIThread.Post(() => vm.AppendLog(line))
                );

                // Always download FLAC to a temp dir — it feeds the separator at full quality.
                // KeepSourceFile is handled after separation (transcode to chosen format).
                dlTempDir = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
                Directory.CreateDirectory(dlTempDir);

                (inputFile, sourceInfo) = await DownloadAudioAsync(
                    url,
                    dlTempDir,
                    dlLog,
                    cts.Token,
                    vm.Job.PreResolvedMeta
                );
                var downloadedName = Path.GetFileName(inputFile);
                Dispatcher.UIThread.Post(() => vm.InputFileName = downloadedName);
            }
            else
            {
                inputFile =
                    vm.Job.InputFilePath
                    ?? throw new InvalidOperationException(
                        "Job has neither a file path nor a source URL."
                    );
                // Read existing tags and cover art from the local source file.
                sourceInfo = AudioTagger.ReadFromFile(inputFile);
            }

            // ── Separation step — one driver run per preset ────────────────────
            var version = _appInfo.FullVersion;
            var presets = vm.Job.Presets;
            var format = FfmpegArgs.Extension(vm.Job.StemOutputFormat).ToUpperInvariant();
            var totalSteps = presets.Count + (vm.Job.ExtractDrums ? 1 : 0);

            for (int i = 0; i < presets.Count; i++)
            {
                int presetIndex = i;
                var preset = presets[i];

                var presetLabel = EffectiveLabel(preset);
                Dispatcher.UIThread.Post(() =>
                {
                    vm.PresetCounter = totalSteps > 1 ? $"{presetIndex + 1}/{totalSteps}" : "";
                    vm.StatusText = $"{presetLabel} — Starting…";
                });

                var progress = new Progress<JobProgress>(p =>
                    Dispatcher.UIThread.Post(() =>
                        HandleProgress(vm, p, presetIndex, totalSteps, presetLabel)
                    )
                );

                var request = BuildRequest(preset, inputFile, vm.Job.OutputDir, format);
                var result = await _driver.RunAsync(request, progress, cts.Token);

                if (!result.Succeeded)
                    throw new InvalidOperationException(result.ErrorMessage ?? "Separation failed");

                // Each output is tagged with only the preset that produced it (display name).
                var presetDescriptor = preset.DisplayName;
                foreach (var o in result.Outputs)
                {
                    outputFiles.Add(o.Path);
                    AudioTagger.ApplyToFile(o.Path, sourceInfo, presetDescriptor, version);
                }

                // Advance the bar to the end of this preset's segment.
                Dispatcher.UIThread.Post(() =>
                {
                    vm.Progress = (int)Math.Round((presetIndex + 1) * 100.0 / totalSteps);
                });
            }

            // ── Drum extraction step ───────────────────────────────────────────
            if (vm.Job.ExtractDrums)
            {
                int drumIndex = presets.Count;
                Dispatcher.UIThread.Post(() =>
                {
                    vm.PresetCounter = $"{drumIndex + 1}/{totalSteps}";
                    vm.StatusText = "Drums — Starting…";
                });

                var drumOutDir =
                    _settings.DrumStemLocation == DrumStemLocation.WithStems
                        ? vm.Job.OutputDir
                        : _paths.DrumCacheDirectory;

                Directory.CreateDirectory(drumOutDir);

                var drumProgress = new Progress<JobProgress>(p =>
                    Dispatcher.UIThread.Post(() =>
                        HandleProgress(vm, p, drumIndex, totalSteps, "Drums")
                    )
                );

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

                var drumResult = await _driver.RunAsync(drumRequest, drumProgress, cts.Token);

                if (drumResult.Succeeded)
                {
                    var drumStem = drumResult.Outputs.FirstOrDefault(o =>
                        o.Stem.Equals("Drums", StringComparison.OrdinalIgnoreCase)
                    );

                    // Delete all non-Drums files written by the model (htdemucs writes all 4 stems
                    // to disk regardless of StemsToKeep; clean up the ones we don't want).
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
                        // Rename to the clean "{title} (Drums).ext" pattern — Demucs ignores
                        // CustomOutputNames, so we fix the filename in C# after the fact.
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

                        // Drum extraction is modelled as a first-class single-model preset so its
                        // output is tagged with a proper display name and per-model provenance,
                        // consistent with the separation presets above.
                        var drumPreset = Preset.DrumExtraction(_settings.DrumExtractionModel);
                        AudioTagger.ApplyToFile(
                            renamedPath,
                            sourceInfo,
                            drumPreset.DisplayName,
                            version
                        );
                        if (_settings.DrumStemLocation == DrumStemLocation.WithStems)
                            outputFiles.Add(renamedPath);
                    }
                }
                else
                {
                    AppLogger.Warning("job", $"Drum extraction failed: {drumResult.ErrorMessage}");
                }

                Dispatcher.UIThread.Post(() => vm.Progress = 100);
            }

            // ── Keep source file step ──────────────────────────────────────────
            if (vm.Job.KeepSourceFile && vm.Job.SourceUrl is not null)
            {
                Dispatcher.UIThread.Post(() => vm.StatusText = "Saving source…");
                var keptSource = await KeepSourceFileAsync(
                    inputFile,
                    vm.Job.StemOutputFormat,
                    vm.Job.OutputDir,
                    cts.Token
                );
                if (keptSource is not null)
                {
                    // The kept source is the unprocessed original, not a separation output —
                    // no preset produced it, so it carries no preset descriptor.
                    AudioTagger.ApplyToFile(
                        keptSource,
                        sourceInfo,
                        presetDescriptor: null,
                        version
                    );
                    outputFiles.Add(keptSource);
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                vm.OutputFiles.AddRange(outputFiles);
                vm.Progress = 100;
                vm.StatusText =
                    $"{outputFiles.Count} stem{(outputFiles.Count == 1 ? "" : "s")} written";
                vm.Status = JobStatus.Done;
                vm.IsExpanded = true;
                OnPropertyChanged();
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                vm.StatusText = "Cancelled";
                vm.Status = JobStatus.Cancelled;
                OnPropertyChanged();
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("job", $"Job failed — {ex.Message}");
            Dispatcher.UIThread.Post(() =>
            {
                vm.ErrorMessage = ex.Message;
                vm.StatusText = "Failed";
                vm.Status = JobStatus.Failed;
                vm.IsExpanded = true;
                OnPropertyChanged();
            });
        }
        finally
        {
            if (dlTempDir is not null)
                try
                {
                    Directory.Delete(dlTempDir, recursive: true);
                }
                catch { }
            _currentJob = null;
            _currentCts = null;
            _gate.Release();
        }
    }

    // ── Progress mapping ──────────────────────────────────────────────────────

    private static void HandleProgress(
        JobItemViewModel vm,
        JobProgress p,
        int presetIndex,
        int totalSteps,
        string presetLabel
    )
    {
        var prefix = presetLabel;

        switch (p.Phase)
        {
            case "downloading_model":
                if (p.Cached == false)
                    vm.StatusText =
                        p.ModelCount > 1
                            ? $"{prefix} — Downloading model {p.ModelIndex}/{p.ModelCount}…"
                            : $"{prefix} — Downloading model…";
                break;

            case "loading_model":
                vm.StatusText =
                    p.ModelCount > 1
                        ? $"{prefix} — Loading model {p.ModelIndex}/{p.ModelCount}…"
                        : $"{prefix} — Loading model…";
                break;

            case "separating":
                vm.StatusText =
                    p.ModelCount > 1
                        ? $"{prefix} — Separating ({p.ModelCount} models)…"
                        : $"{prefix} — Separating…";
                break;

            case "progress":
                if (p.Total is > 0 && p.Current is { } cur)
                {
                    var withinPreset = Math.Min(100, cur * 100 / p.Total.Value);
                    var overall = (int)
                        Math.Round((presetIndex * 100.0 + withinPreset) / totalSteps);
                    vm.Progress = Math.Max(vm.Progress, overall);
                    if (
                        !vm.StatusText.Contains("Separating")
                        && !vm.StatusText.Contains("Combining")
                    )
                        vm.StatusText = $"{prefix} — Separating…";
                }
                break;

            case "ensembling":
                vm.StatusText = p.Stem is { } stem
                    ? $"{prefix} — Combining {stem}…"
                    : $"{prefix} — Combining stems…";
                break;

            case "stem_written":
                if (p.OutputPath is { } path)
                    vm.AppendLog(path);
                break;

            case "log":
                if (p.LogMessage is { Length: > 0 } msg)
                    vm.AppendLog(msg);
                break;
        }
    }

    // ── Request builder ───────────────────────────────────────────────────────

    private static JobRequest BuildRequest(
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

    private static Dictionary<string, string> BuildPresetOutputNames(
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

    private static string EffectiveLabel(Preset preset) =>
        preset.Mode == SeparationMode.BuiltinPreset
            ? $"{CategoryName(preset.Category)} {preset.Label}"
            : preset.Label;

    private static string CategoryName(PresetCategory category) =>
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

    private static string SanitizeLabel(string label) =>
        string.Concat(label.Select(c => _invalidFileNameChars.Contains(c) ? '-' : c)).Trim();

    // ── Cancel / clear ────────────────────────────────────────────────────────

    private void OnCancelRequested(JobItemViewModel vm)
    {
        if (_currentJob == vm)
            _currentCts?.Cancel();
        else if (vm.Status == JobStatus.Queued)
        {
            Dispatcher.UIThread.Post(() =>
            {
                vm.Status = JobStatus.Cancelled;
                vm.StatusText = "Cancelled";
                OnPropertyChanged();
            });
        }
    }

    public void ClearDone()
    {
        var done = Jobs.Where(j => j.IsTerminal).ToList();
        foreach (var j in done)
            Jobs.Remove(j);
        OnPropertyChanged();
    }

    public event EventHandler? StateChanged;

    private void OnPropertyChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    // ── Keep-source helper ────────────────────────────────────────────────────

    /// <summary>
    /// Copies (FLAC) or transcodes (all other formats) the downloaded source FLAC into
    /// <paramref name="outputDir"/> using the user's chosen stem output format.
    /// Metadata embedded by yt-dlp/ffmpeg in the FLAC is carried over automatically.
    /// Returns the destination path, or null on failure (non-fatal).
    /// </summary>
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

    // ── Download helpers ──────────────────────────────────────────────────────

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
        var thumbPath = await _youTubeAudio.DownloadThumbnailAsync(meta.ThumbnailUrl, dlDir, ct);
        var sourceInfo = AudioTagger.FromYtDlpMetadata(meta, thumbPath);
        var audioPath = await _youTubeAudio.DownloadAsync(meta, AudioFormat.Flac, dlDir, log, ct);
        return (audioPath, sourceInfo);
    }
}

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using StemForge.Models;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Runs separation jobs sequentially. One user-submitted job at a time; all others
/// wait in FIFO order. Thread-safe enqueue; all observable mutations happen on the UI thread.
/// </summary>
public sealed partial class JobQueueService(
    ISeparatorDriverService driver,
    AppSettings settings,
    IProcessRunner runner,
    YouTubeAudioService youTubeAudio,
    AppPaths paths
)
{
    private readonly ISeparatorDriverService _driver = driver;
    private readonly AppSettings _settings = settings;
    private readonly IProcessRunner _runner = runner;
    private readonly YouTubeAudioService _youTubeAudio = youTubeAudio;
    private readonly AppPaths _paths = paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _currentCts;
    private JobItemViewModel? _currentJob;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    public int ActiveCount => Jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Queued);

    public void Enqueue(JobRecord record)
    {
        var vm = new JobItemViewModel(record, _settings.MaxJobLogLines)
        {
            CancelRequested = OnCancelRequested,
        };

        Dispatcher.UIThread.Post(() => Jobs.Add(vm));

        _ = RunWhenReadyAsync(vm);
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

                string downloadDir;
                if (vm.Job.KeepSourceFile)
                {
                    downloadDir = vm.Job.OutputDir;
                    Directory.CreateDirectory(downloadDir);
                }
                else
                {
                    dlTempDir = Path.Combine(
                        Path.GetTempPath(),
                        $"stemforge-dl-{Guid.NewGuid():N}"
                    );
                    Directory.CreateDirectory(dlTempDir);
                    downloadDir = dlTempDir;
                }

                inputFile = await DownloadAudioAsync(url, downloadDir, dlLog, cts.Token);
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
            }

            // ── Separation step — one driver run per preset ────────────────────
            var presets = vm.Job.Presets;
            var format = FfmpegArgs.Extension(vm.Job.StemOutputFormat).ToUpperInvariant();

            for (int i = 0; i < presets.Count; i++)
            {
                int presetIndex = i;
                int presetCount = presets.Count;
                var preset = presets[i];

                Dispatcher.UIThread.Post(() =>
                {
                    vm.PresetCounter = $"{presetIndex + 1}/{presetCount}";
                    vm.StatusText = "Starting…";
                });

                var progress = new Progress<JobProgress>(p =>
                    Dispatcher.UIThread.Post(() => HandleProgress(vm, p, presetIndex, presetCount))
                );

                var request = BuildRequest(preset, inputFile, vm.Job.OutputDir, format);
                var result = await _driver.RunAsync(request, progress, cts.Token);

                if (!result.Succeeded)
                    throw new InvalidOperationException(result.ErrorMessage ?? "Separation failed");

                outputFiles.AddRange(result.Outputs.Select(o => o.Path));

                // Advance the bar to the end of this preset's segment.
                Dispatcher.UIThread.Post(() =>
                {
                    vm.Progress = (int)Math.Round((presetIndex + 1) * 100.0 / presetCount);
                });
            }

            // ── Tag step ───────────────────────────────────────────────────────
            await TagSeparationOutputsAsync(outputFiles, inputFile, vm.Job.Presets, cts.Token);

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
        int presetCount
    )
    {
        switch (p.Phase)
        {
            case "downloading_model":
                if (p.Cached == false)
                    vm.StatusText =
                        p.ModelCount > 1
                            ? $"Downloading model {p.ModelIndex}/{p.ModelCount}…"
                            : "Downloading model…";
                break;

            case "loading_model":
                vm.StatusText =
                    p.ModelCount > 1
                        ? $"Loading model {p.ModelIndex}/{p.ModelCount}…"
                        : "Loading model…";
                break;

            case "separating":
                vm.StatusText =
                    p.ModelCount > 1 ? $"Separating ({p.ModelCount} models)…" : "Separating…";
                break;

            case "progress":
                if (p.Total is > 0 && p.Current is { } cur)
                {
                    var withinPreset = Math.Min(100, cur * 100 / p.Total.Value);
                    var overall = (int)
                        Math.Round((presetIndex * 100.0 + withinPreset) / presetCount);
                    vm.Progress = Math.Max(vm.Progress, overall);
                }
                break;

            case "ensembling":
                vm.StatusText = p.Stem is { } stem ? $"Combining {stem}…" : "Combining stems…";
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
                Algorithm: null
            ),
        };

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

    // ── Provenance tagging ────────────────────────────────────────────────────

    private async Task TagSeparationOutputsAsync(
        IReadOnlyList<string> stemPaths,
        string sourceFile,
        IReadOnlyList<Preset> presets,
        CancellationToken ct
    )
    {
        if (stemPaths.Count == 0)
            return;

        var version =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "dev";
        var separationDate = DateTimeOffset.UtcNow.ToString(
            "o",
            System.Globalization.CultureInfo.InvariantCulture
        );
        var modelDescriptor = string.Join(
            ", ",
            presets.Select(p => $"{p.Mode}:{p.PrimaryModel ?? p.Id}")
        );

        var tags = new (string, string)[]
        {
            ("source_file", Path.GetFileName(sourceFile)),
            ("separation_model", modelDescriptor),
            ("separation_date", separationDate),
            ("tool", $"stemforge/{version}"),
        };

        foreach (var stem in stemPaths)
        {
            try
            {
                var ext = Path.GetExtension(stem);
                var tmp = Path.ChangeExtension(stem, null) + ".tmp" + ext;
                var args = new List<string>();
                args.AddRange(FfmpegArgs.Baseline);
                args.AddRange(["-i", stem, "-c", "copy"]);
                args.AddRange(FfmpegArgs.Metadata(tags));
                args.Add(tmp);

                await _runner.RunCheckedAsync(_paths.Ffmpeg, args, ct, logRawLines: false);
                File.Move(tmp, stem, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    "provenance",
                    $"Failed to tag {Path.GetFileName(stem)}: {ex.Message}"
                );
            }
        }
    }

    // ── Download helpers ──────────────────────────────────────────────────────

    private async Task<string> DownloadAudioAsync(
        string url,
        string dlDir,
        IProgress<string> log,
        CancellationToken ct
    )
    {
        var meta = await _youTubeAudio.ResolveAsync(NormalizeUrl(url), _settings, log, ct);
        return await _youTubeAudio.DownloadAsync(meta, AudioFormat.Flac, dlDir, log, ct);
    }

    internal static string NormalizeUrl(string url) =>
        YtVideoIdRegex().Match(url).Groups["VideoId"] is { Success: true, Value: { } id }
            ? $"https://music.youtube.com/watch?v={id}"
            : url;

    [GeneratedRegex(
        @"^(?:(?:(?:https?:\/\/)?(?:(?:www|music|m)\.)?)?(?:youtube\.com|youtu\.be)(?:\S*?(?:\?v=|\/)))?(?<VideoId>[0-9A-Za-z_-]{11})(?:[&?].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    )]
    private static partial Regex YtVideoIdRegex();
}

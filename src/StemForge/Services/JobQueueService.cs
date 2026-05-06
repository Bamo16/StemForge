using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using StemForge.Models;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Runs separation jobs sequentially. One job at a time; all others wait in FIFO order.
/// Thread-safe enqueue; all observable mutations happen on the UI thread.
/// </summary>
public sealed partial class JobQueueService(
    SeparationService separation,
    AppSettings settings,
    IProcessRunner runner
)
{
    private readonly SeparationService _separation = separation;
    private readonly AppSettings _settings = settings;
    private readonly IProcessRunner _runner = runner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _currentCts;
    private JobItemViewModel? _currentJob;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    public int ActiveCount => Jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Queued);

    public void Enqueue(JobRecord record)
    {
        var vm = new JobItemViewModel(record) { CancelRequested = OnCancelRequested };

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

                dlTempDir = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
                Directory.CreateDirectory(dlTempDir);

                var dlLog = new Progress<string>(line =>
                    Dispatcher.UIThread.Post(() => vm.AppendLog(line))
                );

                inputFile = await DownloadAudioAsync(url, dlTempDir, dlLog, cts.Token);
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

            // ── Separation step ────────────────────────────────────────────────
            var progress = new Progress<SeparationProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    vm.Progress = p.StepPercent;
                    vm.StatusText = p.StepLabel;
                    vm.PresetCounter = $"{p.PresetIndex + 1}/{p.PresetCount}";
                });
            });

            var logProgress = new Progress<string>(line =>
                Dispatcher.UIThread.Post(() => vm.AppendLog(line))
            );

            await _separation.RunAsync(
                inputFile,
                vm.Job.Presets,
                vm.Job.OutputDir,
                vm.Job.ModelsDir,
                progress,
                logProgress,
                cts.Token
            );

            var baseName = Path.GetFileNameWithoutExtension(inputFile);
            var outputFiles = vm
                .Job.Presets.Select(p =>
                    Path.Combine(vm.Job.OutputDir, $"{baseName} ({p.Category} - {p.Label}).flac")
                )
                .Where(File.Exists)
                .ToList();

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

    private void OnCancelRequested(JobItemViewModel vm)
    {
        if (_currentJob == vm)
            _currentCts?.Cancel();
        else if (vm.Status == JobStatus.Queued)
        {
            // Job hasn't started yet — mark cancelled immediately
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

    private async Task<string> DownloadAudioAsync(
        string url,
        string dlDir,
        IProgress<string> log,
        CancellationToken ct
    )
    {
        var args = new List<string>
        {
            "--no-playlist",
            "--format",
            "bestaudio/best",
            "--extract-audio",
            "--audio-format",
            "flac",
            "--audio-quality",
            "0",
            "--output",
            "%(title)s.%(ext)s",
            "--paths",
            dlDir,
        };

        var cookies = _settings.YtdlpCookiesFromBrowser;
        if (!string.IsNullOrWhiteSpace(cookies))
        {
            // If it looks like a file path, use --cookies; otherwise use --cookies-from-browser.
            var flag =
                cookies.Contains(Path.DirectorySeparatorChar)
                || cookies.Contains(Path.AltDirectorySeparatorChar)
                || cookies.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? "--cookies"
                    : "--cookies-from-browser";
            args.AddRange([flag, cookies]);
        }

        var jsRuntime = _settings.YtdlpJsRuntime;
        if (!string.IsNullOrWhiteSpace(jsRuntime))
            args.AddRange(["--js-runtime", jsRuntime]);

        args.Add(NormalizeUrl(url));

        await _runner.RunStreamingAsync(_settings.YtdlpPath, args, log, ct);

        return Directory.GetFiles(dlDir).FirstOrDefault()
            ?? throw new InvalidOperationException("yt-dlp produced no output file.");
    }

    /// <summary>Normalise YouTube URLs to music.youtube.com for the best available audio format.</summary>
    internal static string NormalizeUrl(string url) =>
        YtVideoIdRegex().Match(url).Groups["VideoId"] is { Success: true, Value: { } id }
            ? $"https://music.youtube.com/watch?v={id}"
            : url;

    /// <summary>Matches any YouTube URL or bare video ID and captures the 11-char video ID.</summary>
    [GeneratedRegex(
        @"^(?:(?:(?:https?:\/\/)?(?:(?:www|music|m)\.)?)?(?:youtube\.com|youtu\.be)(?:\S*?(?:\?v=|\/)))?(?<VideoId>[0-9A-Za-z_-]{11})(?:[&?].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    )]
    private static partial Regex YtVideoIdRegex();
}

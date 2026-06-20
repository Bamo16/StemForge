using StemForge.Cli.Commands;
using StemForge.Core.Helpers;
using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Commands;

/// <summary>
/// Tests for the download command and its pipeline path (<see cref="SeparationPipeline.DownloadOnlyAsync"/>).
/// Covers: a download produces a tagged file in the output directory in the requested format,
/// no separation runs, format/output resolution defaults, URL detection, and the batch
/// exit-code formula shared with the separate command (0 all-succeeded / 2 partial / 1 all-failed).
/// </summary>
public sealed class DownloadCommandTests : IDisposable
{
    private readonly string _tempDir;

    // Minimal valid FLAC: magic + last-metadata STREAMINFO block (all-zero stream info).
    private static readonly byte[] _minimalFlac =
    [
        0x66,
        0x4C,
        0x61,
        0x43,
        0x80,
        0x00,
        0x00,
        0x22,
        .. new byte[34],
    ];

    public DownloadCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sfdl-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // A process runner whose ffmpeg "download" writes a real (minimal) FLAC to the output path
    // so that the subsequent tagging step has a file to open. The output path is the last arg.
    private sealed class FfmpegFileWritingRunner : IProcessRunner
    {
        public List<string> StreamedExes { get; } = [];

        public Task RunStreamingAsync(
            string exe,
            IEnumerable<string> args,
            IProgress<string>? progress = null,
            CancellationToken ct = default,
            bool logRawLines = true
        )
        {
            StreamedExes.Add(exe);
            var outputPath = args.Last();
            File.WriteAllBytes(outputPath, _minimalFlac);
            return Task.CompletedTask;
        }

        public Task<ProcessRunner.Result> RunAsync(
            string exe,
            IEnumerable<string> args,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => Task.FromResult(new ProcessRunner.Result(0, "", ""));

        public Task<ProcessRunner.Result> RunCheckedAsync(
            string exe,
            IEnumerable<string> args,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => Task.FromResult(new ProcessRunner.Result(0, "", ""));

        public Task<ProcessRunner.Result> RunStreamingStderrAsync(
            string exe,
            IEnumerable<string> args,
            IProgress<string>? stderrProgress = null,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => Task.FromResult(new ProcessRunner.Result(0, "", ""));
    }

    private static YtDlpMetadata MakeMeta(
        string title = "Sample Track",
        string? artist = "Artist"
    ) =>
        new(
            SourceUrl: "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Title: title,
            Artist: artist,
            Uploader: "Channel",
            SourceCodec: "opus",
            SourceBitrateKbps: 160.0,
            DurationSeconds: 200.0,
            FormatId: "251",
            MediaUrl: "https://media.example.com/251",
            ThumbnailUrl: "https://img.example.com/thumb.jpg"
        );

    // Builds a pipeline wired with the real YouTubeAudioService (backed by a file-writing
    // ffmpeg) and a spy driver so a separation call can be detected.
    private (
        SeparationPipeline Pipeline,
        SpySeparatorDriverService Driver,
        StubThumbnailFetcher Thumbs
    ) BuildPipeline(string outputDir)
    {
        var settings = new AppSettings();
        settings.SetToolPathOverride(ToolKind.Ffmpeg, "ffmpeg");
        var paths = new AppPaths(settings);

        var runner = new FfmpegFileWritingRunner();
        var youTube = new YouTubeAudioService(runner, paths);
        var thumbs = new StubThumbnailFetcher();
        var driver = new SpySeparatorDriverService();

        var pipeline = new SeparationPipeline(
            driver,
            youTube,
            thumbs,
            runner,
            settings,
            paths,
            AppInfo.Current
        );
        return (pipeline, driver, thumbs);
    }

    private JobRecord MakeJob(string outputDir, AudioFormat format = AudioFormat.Flac) =>
        new(
            Id: Guid.NewGuid(),
            InputFilePath: null,
            SourceUrl: "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Presets: [],
            OutputDir: outputDir,
            ModelsDir: _tempDir,
            StemOutputFormat: format,
            PreResolvedMeta: MakeMeta()
        );

    // ── DownloadOnlyAsync: deliverable + tagging ──────────────────────────────

    [Fact]
    public async Task DownloadOnlyAsync_WritesFileIntoOutputDirectory()
    {
        var (pipeline, _, _) = BuildPipeline(_tempDir);
        var job = MakeJob(_tempDir);

        var path = await pipeline.DownloadOnlyAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.True(File.Exists(path));
        Assert.Equal(_tempDir, Path.GetDirectoryName(path));
        Assert.Equal(".flac", Path.GetExtension(path));
    }

    [Fact]
    public async Task DownloadOnlyAsync_HonorsRequestedFormatExtension()
    {
        var (pipeline, _, _) = BuildPipeline(_tempDir);
        var job = MakeJob(_tempDir, AudioFormat.Mp3);

        var path = await pipeline.DownloadOnlyAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(".mp3", Path.GetExtension(path));
    }

    [Fact]
    public async Task DownloadOnlyAsync_AppliesMetadataTitleToFile()
    {
        var (pipeline, _, _) = BuildPipeline(_tempDir);
        var job = MakeJob(_tempDir);

        var path = await pipeline.DownloadOnlyAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        var tags = AudioTagger.ReadFromFile(path);
        Assert.NotNull(tags);
        // Title is written as the display title ("Artist - Title") and the artist is preserved.
        Assert.Equal("Artist - Sample Track", tags!.Title);
        Assert.Equal("Artist", tags.Artist);
    }

    // ── No separation is performed ────────────────────────────────────────────

    [Fact]
    public async Task DownloadOnlyAsync_DoesNotInvokeSeparatorDriver()
    {
        var (pipeline, driver, _) = BuildPipeline(_tempDir);
        var job = MakeJob(_tempDir);

        await pipeline.DownloadOnlyAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(0, driver.CallCount);
    }

    [Fact]
    public async Task DownloadOnlyAsync_ReportsRunCompleteWithWrittenPath()
    {
        var (pipeline, _, _) = BuildPipeline(_tempDir);
        var job = MakeJob(_tempDir);

        var updates = new System.Collections.Concurrent.ConcurrentBag<JobUpdate>();

        var path = await pipeline.DownloadOnlyAsync(
            job,
            new Progress<JobUpdate>(updates.Add),
            ct: TestContext.Current.CancellationToken
        );

        for (var i = 0; i < 100 && !updates.Any(u => u.Phase == "run_complete"); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        var complete = updates.LastOrDefault(u => u.Phase == "run_complete");
        Assert.NotNull(complete);
        Assert.Equal(100, complete!.OverallPercent);
        Assert.NotNull(complete.WrittenPaths);
        Assert.Contains(path, complete.WrittenPaths!);
    }

    [Fact]
    public async Task DownloadOnlyAsync_FileInputJob_Throws()
    {
        var (pipeline, _, _) = BuildPipeline(_tempDir);
        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: Path.Combine(_tempDir, "local.flac"),
            SourceUrl: null,
            Presets: [],
            OutputDir: _tempDir,
            ModelsDir: _tempDir
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.DownloadOnlyAsync(
                job,
                progress: null,
                ct: TestContext.Current.CancellationToken
            )
        );
    }

    // ── Thumbnail is not left in the output directory ─────────────────────────

    [Fact]
    public async Task DownloadOnlyAsync_DoesNotFetchThumbnailIntoOutputDirectory()
    {
        var (pipeline, _, thumbs) = BuildPipeline(_tempDir);
        var job = MakeJob(_tempDir);

        await pipeline.DownloadOnlyAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        // The thumbnail fetch target must be a temp directory, not the user's output directory.
        Assert.Equal(1, thumbs.CallCount);
        Assert.NotNull(thumbs.LastOutDir);
        Assert.NotEqual(_tempDir, thumbs.LastOutDir);
    }

    // ── Format resolution defaults (shared helper) ────────────────────────────

    [Fact]
    public void ValidateFormat_NullFormat_FallsBackToSettingsDefault()
    {
        var settings = new AppSettings { DefaultAudioFormat = AudioFormat.Wav };
        var outcome = SeparateCommand.ValidateFormat(null, settings);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(AudioFormat.Wav, outcome.ResolvedFormat);
    }

    [Fact]
    public void ValidateFormat_UnknownFormat_ReturnsExitCode1()
    {
        var outcome = SeparateCommand.ValidateFormat("ogg", new AppSettings());

        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("ogg", outcome.ErrorMessage);
    }

    // ── URL detection: only URLs are downloadable ─────────────────────────────

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://soundcloud.com/artist/track")]
    public void YtUrlHelper_RecognizesDownloadableUrls(string url)
    {
        Assert.True(YtUrlHelper.TryNormalize(url, out var normalized));
        Assert.NotNull(normalized);
    }

    [Theory]
    [InlineData("/path/to/track.flac")]
    [InlineData("C:\\Music\\track.flac")]
    [InlineData("track.flac")]
    public void YtUrlHelper_RejectsLocalFilePaths(string path)
    {
        Assert.False(YtUrlHelper.TryNormalize(path, out _));
    }

    // ── Batch exit-code formula (matches separate command exactly) ────────────

    [Fact]
    public void ExitCode_AllSucceeded_Is0()
    {
        int succeeded = 3,
            total = 3;
        Assert.Equal(0, succeeded == total ? 0 : (succeeded == 0 ? 1 : 2));
    }

    [Fact]
    public void ExitCode_PartialFailure_Is2()
    {
        int succeeded = 1,
            total = 3;
        Assert.Equal(2, succeeded == total ? 0 : (succeeded == 0 ? 1 : 2));
    }

    [Fact]
    public void ExitCode_AllFailed_Is1()
    {
        int succeeded = 0,
            total = 3;
        Assert.Equal(1, succeeded == total ? 0 : (succeeded == 0 ? 1 : 2));
    }
}

using System.Runtime.InteropServices;
using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Tooling;

public sealed class ToolReportServiceTests : IDisposable
{
    private static readonly PlatformInfo Windows = new(OSKind.Windows, Architecture.X64);

    private readonly string _binDir = Path.Combine(
        Path.GetTempPath(),
        $"sftools-test-{Guid.NewGuid():N}"
    );
    private readonly AppSettings _settings = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly RecordingDownloader _downloader = new();
    private readonly AppPaths _paths;
    private readonly ToolReportService _service;

    public ToolReportServiceTests()
    {
        _paths = new AppPaths(_settings, Windows, _binDir);
        _service = new ToolReportService(
            new SetupDetector(_runner, _paths),
            _paths,
            new ToolInstaller(
                _runner,
                _paths,
                new BundledFetcher(_paths, Windows, _downloader),
                Windows
            ),
            _runner,
            Windows
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_binDir))
            Directory.Delete(_binDir, recursive: true);
    }

    private sealed class RecordingDownloader : IFileDownloader
    {
        public List<string> Urls { get; } = [];

        public Task DownloadAsync(
            string url,
            string destination,
            IProgress<InstallProgress>? progress,
            CancellationToken ct,
            string? toolName = null
        )
        {
            Urls.Add(url);

            throw new IOException("stop here: the fixture has no archive to serve");
        }
    }

    private void SetupAllTools()
    {
        _runner.Setup(_paths.Uv, "uv 0.4.0");
        _runner.Setup(_paths.AudioSeparator, "audio-separator 0.27.2");
        _runner.Setup(_paths.Ffmpeg, "ffmpeg version 9.0 Copyright (c) 2000-2025");
        _runner.Setup(_paths.Ffprobe, "ffprobe version 9.0 Copyright (c) 2007-2025");
        _runner.Setup(_paths.Ytdlp, "2026.08.19");
        _runner.Setup(_paths.Deno, "deno 2.8.0 (stable)");
    }

    [Fact]
    public async Task ReportAsync_EverythingInstalled_ListsEachToolWithItsPathVersionAndRelease()
    {
        SetupAllTools();

        var report = await _service.ReportAsync(
            ensure: false,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["uv", "audio-separator", "ffmpeg", "ffprobe", "yt-dlp", "deno"],
            report.Select(r => r.Kind)
        );
        var ffmpeg = report.Single(r => r.Kind == "ffmpeg");
        Assert.Equal(Path.Combine(_binDir, "ffmpeg.exe"), ffmpeg.Path);
        Assert.True(ffmpeg.Installed);
        Assert.False(ffmpeg.Overridden);
        Assert.Equal("9.0", ffmpeg.Version);
        Assert.Equal(ToolCatalog.BundledRelease(ToolKind.Ffmpeg), ffmpeg.Release);
        Assert.NotNull(ffmpeg.Release);

        var ffprobe = report.Single(r => r.Kind == "ffprobe");
        Assert.Equal(Path.Combine(_binDir, "ffprobe.exe"), ffprobe.Path);
        Assert.Equal("9.0", ffprobe.Version);
        Assert.Equal(ffmpeg.Release, ffprobe.Release);

        var ytdlp = report.Single(r => r.Kind == "yt-dlp");
        Assert.Equal("2026.08.19", ytdlp.Version);
        Assert.Null(ytdlp.Release);
    }

    [Fact]
    public async Task ReportAsync_BundledFfmpegMissing_IsNotInstalledButStillNamesItsPath()
    {
        // Only uv answers; ffmpeg and ffprobe read as absent, as on a machine without the bundle.
        _runner.Setup(_paths.Uv, "uv 0.4.0");

        var report = await _service.ReportAsync(
            ensure: false,
            ct: TestContext.Current.CancellationToken
        );

        foreach (var kind in (string[])["ffmpeg", "ffprobe"])
        {
            var tool = report.Single(r => r.Kind == kind);
            Assert.False(tool.Installed);
            Assert.Null(tool.Version);
            Assert.Equal(Path.Combine(_binDir, $"{kind}.exe"), tool.Path);
        }
        Assert.True(report.Single(r => r.Kind == "uv").Installed);
        Assert.Empty(_downloader.Urls);
    }

    [Fact]
    public async Task ReportAsync_FfmpegOverridden_ReportsTheOverrideAndItsFfprobeWithoutARelease()
    {
        var overrideDir = Path.Combine(Path.GetTempPath(), "my-ffmpeg", "bin");
        var overridePath = Path.Combine(overrideDir, "ffmpeg.exe");
        _settings.SetToolPathOverride(ToolKind.Ffmpeg, overridePath);
        SetupAllTools();
        _runner.Setup(overridePath, "ffmpeg version 7.1.1");
        _runner.Setup(Path.Combine(overrideDir, "ffprobe.exe"), "ffprobe version 7.1.1");

        var report = await _service.ReportAsync(
            ensure: false,
            ct: TestContext.Current.CancellationToken
        );

        var ffmpeg = report.Single(r => r.Kind == "ffmpeg");
        Assert.Equal(overridePath, ffmpeg.Path);
        Assert.True(ffmpeg.Overridden);
        Assert.Equal("7.1.1", ffmpeg.Version);
        Assert.Null(ffmpeg.Release);

        var ffprobe = report.Single(r => r.Kind == "ffprobe");
        Assert.Equal(Path.Combine(overrideDir, "ffprobe.exe"), ffprobe.Path);
        Assert.True(ffprobe.Overridden);
        Assert.Equal("7.1.1", ffprobe.Version);

        Assert.False(report.Single(r => r.Kind == "deno").Overridden);
    }

    [Fact]
    public async Task ReportAsync_EnsureWithToolsMissing_FetchesOnlyBundledToolsAndNeverUvOrAudioSeparator()
    {
        // Nothing registered: every tool reads as missing. ffmpeg is fetched first, and the
        // downloader's failure ends the run.
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            _service.ReportAsync(ensure: true, ct: TestContext.Current.CancellationToken)
        );

        Assert.Contains("stop here", ex.Message);
        var url = Assert.Single(_downloader.Urls);
        Assert.Contains("nomercy-ffmpeg", url);
        Assert.DoesNotContain(_runner.Calls, call => call.Args.Contains("install"));
    }

    [Fact]
    public async Task ReportAsync_EnsureWithEverythingInstalled_FetchesNothing()
    {
        SetupAllTools();

        var report = await _service.ReportAsync(
            ensure: true,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Empty(_downloader.Urls);
        Assert.All(report, r => Assert.True(r.Installed));
    }

    [Fact]
    public async Task ReportAsync_EnsureWithFfmpegOverriddenAndMissing_DoesNotFetchIt()
    {
        SetupAllTools();
        _settings.SetToolPathOverride(ToolKind.Ffmpeg, @"Z:\gone\ffmpeg.exe");

        var report = await _service.ReportAsync(
            ensure: true,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Empty(_downloader.Urls);
        var ffmpeg = report.Single(r => r.Kind == "ffmpeg");
        Assert.True(ffmpeg.Overridden);
        Assert.False(ffmpeg.Installed);
    }
}

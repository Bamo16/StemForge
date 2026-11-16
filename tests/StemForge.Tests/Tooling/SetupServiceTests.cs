using System.Runtime.InteropServices;
using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Tooling;

public sealed class SetupServiceTests : IDisposable
{
    private static readonly PlatformInfo Windows = new(OSKind.Windows, Architecture.X64);
    private static readonly PlatformInfo Linux = new(OSKind.Linux, Architecture.X64);

    private readonly string _binDir = Path.Combine(
        Path.GetTempPath(),
        $"sfsetup-test-{Guid.NewGuid():N}"
    );
    private readonly AppSettings _settings = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly AppPaths _paths;

    public SetupServiceTests() => _paths = new AppPaths(_settings, Windows, _binDir);

    public void Dispose()
    {
        if (Directory.Exists(_binDir))
            Directory.Delete(_binDir, recursive: true);
    }

    private sealed class FailingDownloader : IFileDownloader
    {
        public int Downloads { get; private set; }

        public Task DownloadAsync(
            string url,
            string destination,
            IProgress<InstallProgress>? progress,
            CancellationToken ct,
            string? toolName = null
        )
        {
            Downloads++;

            throw new IOException("offline");
        }
    }

    private SetupService NewService(FailingDownloader downloader, PlatformInfo? platform = null)
    {
        var os = platform ?? Windows;

        return new SetupService(
            new SetupDetector(_runner, _paths),
            new GpuDetector(_runner),
            new ToolInstaller(_runner, _paths, new BundledFetcher(_paths, os, downloader), os),
            _settings,
            os
        );
    }

    private void SetupAllTools(bool withYtdlp = true)
    {
        _runner.Setup(_paths.Uv, "uv 0.4.0");
        _runner.Setup(_paths.AudioSeparator, "audio-separator 0.27.2");
        _runner.Setup(_paths.Ffmpeg, "ffmpeg version 9.0 Copyright (c) 2000-2025");
        if (withYtdlp)
            _runner.Setup(_paths.Ytdlp, "2026.08.19");
        _runner.Setup(_paths.Deno, "deno 2.8.0 (stable)");
    }

    private IEnumerable<string> InstallCalls() =>
        _runner
            .Calls.Where(call => call.Args is ["tool", "install", ..])
            .Select(call => string.Join(' ', call.Args));

    [Fact]
    public async Task DetectAsync_NoGpuFound_SuggestsCpuAndOffersEveryWindowsVariant()
    {
        var detection = await NewService(new FailingDownloader()).DetectAsync();

        Assert.Equal(GpuVariant.Cpu, detection.Suggested);
        Assert.Equal([GpuVariant.Cuda, GpuVariant.DirectML, GpuVariant.Cpu], detection.Available);
    }

    [Fact]
    public async Task RunAsync_EverythingInstalledWithTheSameVariant_InstallsNothing()
    {
        SetupAllTools();
        _settings.InstalledVariant = GpuVariant.Cuda;
        var downloader = new FailingDownloader();

        var result = await NewService(downloader)
            .RunAsync(GpuVariant.Cuda, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.All(result.Steps, step => Assert.Equal(SetupStepStatus.Skipped, step.Status));
        Assert.Equal(
            ["uv", "audio-separator", "ffmpeg", "yt-dlp", "deno"],
            result.Steps.Select(step => step.Tool)
        );
        Assert.Empty(InstallCalls());
        Assert.Equal(0, downloader.Downloads);
    }

    [Fact]
    public async Task RunAsync_InstalledVariantDiffers_ReinstallsAudioSeparatorWithTheChosenOne()
    {
        SetupAllTools();
        _settings.InstalledVariant = GpuVariant.Cpu;

        var result = await NewService(new FailingDownloader())
            .RunAsync(GpuVariant.Cuda, ct: TestContext.Current.CancellationToken);

        Assert.Equal(
            SetupStepStatus.Installed,
            result.Steps.Single(step => step.Tool == "audio-separator").Status
        );
        Assert.Single(result.Steps, step => step.Status is SetupStepStatus.Installed);
        var install = Assert.Single(InstallCalls());
        Assert.Contains("audio-separator[gpu]", install);
        Assert.Equal(GpuVariant.Cuda, _settings.InstalledVariant);
        Assert.Equal(GpuVariant.Cuda, _settings.GpuVariant);
    }

    [Fact]
    public async Task RunAsync_BundledToolMissing_IsFetchedAndAFailureFailsTheRunWithoutFinishingSetup()
    {
        SetupAllTools(withYtdlp: false);
        _settings.InstalledVariant = GpuVariant.Cpu;
        var downloader = new FailingDownloader();
        List<SetupStep> reported = [];

        var result = await NewService(downloader)
            .RunAsync(
                GpuVariant.Cpu,
                new SyncProgress<SetupStep>(reported.Add),
                ct: TestContext.Current.CancellationToken
            );

        Assert.False(result.Succeeded);
        var failed = Assert.Single(result.Steps, step => step.Status is SetupStepStatus.Failed);
        Assert.Equal("yt-dlp", failed.Tool);
        Assert.Equal("offline", failed.Message);
        Assert.Equal(1, downloader.Downloads);
        Assert.Equal(
            [SetupStepStatus.Installing, SetupStepStatus.Failed],
            reported.Where(step => step.Tool == "yt-dlp").Select(step => step.Status)
        );
        Assert.False(_settings.FirstRunComplete);
    }

    [Fact]
    public async Task RunAsync_EverythingSucceeds_MarksFirstRunComplete()
    {
        SetupAllTools();
        _settings.InstalledVariant = GpuVariant.Cpu;

        await NewService(new FailingDownloader())
            .RunAsync(GpuVariant.Cpu, ct: TestContext.Current.CancellationToken);

        Assert.True(_settings.FirstRunComplete);
        Assert.Equal(GpuVariant.Cpu, _settings.GpuVariant);
    }

    [Fact]
    public async Task RunAsync_VariantTheOsDoesNotOffer_IsRefusedBeforeAnythingIsInstalled()
    {
        var service = NewService(new FailingDownloader(), Linux);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RunAsync(GpuVariant.DirectML, ct: TestContext.Current.CancellationToken)
        );

        Assert.Empty(_runner.Calls);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

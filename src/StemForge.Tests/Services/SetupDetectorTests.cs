using StemForge.Models;
using StemForge.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Services;

public sealed class SetupDetectorTests
{
    // The key used in DetectAllAsync — may be a full shim path if audio-separator is installed.
    private static readonly string AudioSeparatorExe = SetupDetector.ResolveAudioSeparatorPath();

    [Theory]
    [InlineData(GpuVariant.Cuda, "gpu")]
    [InlineData(GpuVariant.DirectML, "dml")]
    [InlineData(GpuVariant.Cpu, "cpu")]
    public void GetPipExtra_ReturnsCorrectExtra(GpuVariant variant, string expected)
    {
        Assert.Equal(expected, SetupDetector.GetPipExtra(variant));
    }

    [Fact]
    public async Task DetectAllAsync_AllFound_ReturnsFoundResults()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("uv", "uv 0.4.0");
        fake.Setup(AudioSeparatorExe, "audio-separator 0.27.2");
        fake.Setup("yt-dlp", "2024.12.13");
        fake.Setup("ffmpeg", "ffmpeg version 7.0");

        var detector = new SetupDetector(fake);
        var results = await detector.DetectAllAsync(ytdlpPath: null);

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.True(r.Found));
    }

    [Fact]
    public async Task DetectAllAsync_UvMissing_MarksUvNotFound()
    {
        var fake = new FakeProcessRunner();
        fake.Setup(AudioSeparatorExe, "audio-separator 0.27.2");
        fake.Setup("yt-dlp", "2024.12.13");
        fake.Setup("ffmpeg", "ffmpeg version 7.0");
        // uv not registered → throws → Found = false

        var detector = new SetupDetector(fake);
        var results = await detector.DetectAllAsync(ytdlpPath: null);

        var uv = results.Single(r => r.Name == "uv");
        Assert.False(uv.Found);
        Assert.True(uv.IsRequired);
    }

    [Fact]
    public async Task DetectAllAsync_OptionalToolsMissing_AllSystemsGoStillPossible()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("uv", "uv 0.4.0");
        fake.Setup(AudioSeparatorExe, "audio-separator 0.27.2");
        // yt-dlp and ffmpeg not registered → throw → Found = false

        var detector = new SetupDetector(fake);
        var results = await detector.DetectAllAsync(ytdlpPath: null);

        var ytdlp = results.Single(r => r.Name == "yt-dlp");
        var ffmpeg = results.Single(r => r.Name == "ffmpeg");

        Assert.False(ytdlp.Found);
        Assert.False(ytdlp.IsRequired);
        Assert.False(ffmpeg.Found);
        Assert.False(ffmpeg.IsRequired);

        // Required tools are found — AllSystemsGo logic would pass
        Assert.True(results.All(r => r.Found || !r.IsRequired));
    }

    [Fact]
    public async Task DetectAllAsync_CustomYtdlpPath_UsesProvidedPath()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("uv", "uv 0.4.0");
        fake.Setup(AudioSeparatorExe, "audio-separator 0.27.2");
        fake.Setup(@"C:\Tools\yt-dlp.exe", "2024.12.13");
        fake.Setup("ffmpeg", "ffmpeg version 7.0");

        var detector = new SetupDetector(fake);
        var results = await detector.DetectAllAsync(ytdlpPath: @"C:\Tools\yt-dlp.exe");

        var ytdlp = results.Single(r => r.Name == "yt-dlp");
        Assert.True(ytdlp.Found);
        Assert.Equal("2024.12.13", ytdlp.Version);
    }
}

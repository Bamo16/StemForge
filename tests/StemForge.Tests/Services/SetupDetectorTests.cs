using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Services;

public sealed class SetupDetectorTests
{
    private static (SetupDetector detector, FakeProcessRunner fake, AppPaths paths) Build()
    {
        var fake = new FakeProcessRunner();
        var settings = new AppSettings();
        var paths = new AppPaths(settings);
        return (new SetupDetector(fake, paths), fake, paths);
    }

    [Fact]
    public async Task DetectAsync_AllFound_ReturnsFoundResults()
    {
        var (detector, fake, paths) = Build();
        fake.Setup(paths.Uv, "uv 0.4.0");
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(paths.Ytdlp, "2024.12.13");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");
        fake.Setup(paths.Deno, "deno 2.8.0 (...)");

        var results = await detector.DetectAsync();

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Found));
        // ToolState wraps the catalog Tool; Name/IsRequired are read off it, not copied.
        Assert.All(results, r => Assert.Same(ToolCatalog.Get(r.Kind), r.Tool));
        Assert.All(results, r => Assert.Equal(r.Tool.CliName, r.Name));
        Assert.All(results, r => Assert.Equal(r.Tool.IsRequired, r.IsRequired));
    }

    [Fact]
    public async Task DetectAsync_UvMissing_MarksUvNotFound()
    {
        var (detector, fake, paths) = Build();
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(paths.Ytdlp, "2024.12.13");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");
        // uv not registered → throws → Found = false

        var results = await detector.DetectAsync();

        var uv = results.Single(r => r.Name == "uv");
        Assert.False(uv.Found);
        Assert.True(uv.IsRequired);
    }

    [Fact]
    public async Task DetectAsync_OnlyYtdlpMissing_AllRequiredToolsPresent()
    {
        var (detector, fake, paths) = Build();
        fake.Setup(paths.Uv, "uv 0.4.0");
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");
        // yt-dlp not registered → throw → Found = false. yt-dlp is the only
        // optional tool now that ffmpeg is required by audio-separator.

        var results = await detector.DetectAsync();

        var ytdlp = results.Single(r => r.Name == "yt-dlp");
        var ffmpeg = results.Single(r => r.Name == "ffmpeg");

        Assert.False(ytdlp.Found);
        Assert.False(ytdlp.IsRequired);
        Assert.True(ffmpeg.Found);
        Assert.True(ffmpeg.IsRequired);
        Assert.True(results.All(r => r.Found || !r.IsRequired));
    }

    [Fact]
    public async Task DetectAsync_CustomYtdlpPath_UsesProvidedPath()
    {
        var fake = new FakeProcessRunner();
        var settings = new AppSettings();
        settings.SetToolPathOverride(ToolKind.Ytdlp, @"C:\Tools\yt-dlp.exe");
        var paths = new AppPaths(settings);
        var detector = new SetupDetector(fake, paths);

        fake.Setup(paths.Uv, "uv 0.4.0");
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(@"C:\Tools\yt-dlp.exe", "2024.12.13");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");

        var results = await detector.DetectAsync();

        var ytdlp = results.Single(r => r.Name == "yt-dlp");
        Assert.True(ytdlp.Found);
        Assert.Equal("2024.12.13", ytdlp.Version);
    }
}

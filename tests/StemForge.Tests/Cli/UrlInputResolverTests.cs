using StemForge.Cli.Progress;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Cli;

/// <summary>
/// Tests resolve-up-front (#52): a URL input is resolved to metadata before any progress is shown,
/// the resolved title (the eventual filename) becomes the input label, the metadata is handed back
/// for reuse as <see cref="JobRecord.PreResolvedMeta"/>, and a resolution failure surfaces as a
/// failed outcome (so the command can mark the input failed before any bar).
/// </summary>
public sealed class UrlInputResolverTests
{
    private const string ValidJson = """
        {
          "title": "Test Track",
          "artist": "Test Artist",
          "thumbnail": "https://img.example.com/thumb.jpg",
          "url": "https://media.example.com/fallback",
          "acodec": "opus",
          "abr": 160.0,
          "duration": 240.0,
          "format_id": "251",
          "formats": [
            {
              "format_id": "251",
              "acodec": "opus",
              "vcodec": "none",
              "abr": 160.0,
              "asr": 48000,
              "url": "https://media.example.com/251"
            }
          ]
        }
        """;

    private static YouTubeAudioService BuildService(IProcessRunner fake)
    {
        var settings = new AppSettings();
        settings.SetToolPathOverride(ToolKind.Ytdlp, "yt-dlp");
        var paths = new AppPaths(settings);
        return new YouTubeAudioService(fake, paths);
    }

    [Fact]
    public async Task ResolveAsync_HappyPath_TitleIsDisplayTitleAndMetaReturned()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("yt-dlp", ValidJson);
        var svc = BuildService(fake);

        var outcome = await UrlInputResolver.ResolveAsync(
            svc,
            "https://youtu.be/abc",
            new AppSettings(),
            TestContext.Current.CancellationToken
        );

        Assert.True(outcome.Succeeded);
        // The label shown to the user is the display title, not the raw URL.
        Assert.Equal("Test Artist - Test Track", outcome.Title);
        Assert.Null(outcome.FailureReason);

        // The metadata is handed back so the command can pass it as PreResolvedMeta. A job built
        // with it carries the title (the pipeline reuses this instead of resolving again).
        Assert.NotNull(outcome.Meta);
        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: null,
            SourceUrl: "https://youtu.be/abc",
            Presets: [],
            OutputDir: "out",
            ModelsDir: "models",
            PreResolvedMeta: outcome.Meta
        );
        Assert.Same(outcome.Meta, job.PreResolvedMeta);
        Assert.Equal("Test Track", job.PreResolvedMeta!.Title);
    }

    [Fact]
    public async Task ResolveAsync_YtDlpFails_ReturnsFailedOutcomeWithReason()
    {
        var fake = new FakeProcessRunner();
        // Non-zero exit makes RunStreamingStderrAsync throw; the resolver maps it to a failure.
        fake.Setup("yt-dlp", new FakeProcessRunner.FakeResult(ExitCode: 1, Stderr: "bad url"));
        var svc = BuildService(fake);

        var outcome = await UrlInputResolver.ResolveAsync(
            svc,
            "https://youtu.be/bad",
            new AppSettings(),
            TestContext.Current.CancellationToken
        );

        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Meta);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public async Task ResolveAsync_Cancelled_Rethrows()
    {
        // A cancellation must propagate (not be swallowed into a failed outcome) so the command can
        // stop the whole batch rather than mark a single input failed.
        var svc = BuildService(new CancellingProcessRunner());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UrlInputResolver.ResolveAsync(
                svc,
                "https://youtu.be/abc",
                new AppSettings(),
                CancellationToken.None
            )
        );
    }

    // A runner that simulates a yt-dlp call interrupted by cancellation.
    private sealed class CancellingProcessRunner : IProcessRunner
    {
        public Task RunStreamingAsync(
            string exe,
            IEnumerable<string> args,
            IProgress<string>? progress = null,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => throw new OperationCanceledException();

        public Task<ProcessRunner.Result> RunAsync(
            string exe,
            IEnumerable<string> args,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => throw new OperationCanceledException();

        public Task<ProcessRunner.Result> RunCheckedAsync(
            string exe,
            IEnumerable<string> args,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => throw new OperationCanceledException();

        public Task<ProcessRunner.Result> RunStreamingStderrAsync(
            string exe,
            IEnumerable<string> args,
            IProgress<string>? stderrProgress = null,
            CancellationToken ct = default,
            bool logRawLines = true
        ) => throw new OperationCanceledException();
    }
}

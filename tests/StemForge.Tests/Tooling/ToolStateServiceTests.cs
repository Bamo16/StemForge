using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Tooling;

public sealed class ToolStateServiceTests
{
    private static (ToolStateService state, FakeProcessRunner fake, AppPaths paths) Build()
    {
        var fake = new FakeProcessRunner();
        var settings = new AppSettings();
        var paths = new AppPaths(settings);
        return (new ToolStateService(new SetupDetector(fake, paths)), fake, paths);
    }

    private static void SetupAll(FakeProcessRunner fake, AppPaths paths)
    {
        fake.Setup(paths.Uv, "uv 0.4.0");
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(paths.Ytdlp, "2024.12.13");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");
        fake.Setup(paths.Deno, "deno 2.8.0 (...)");
    }

    [Fact]
    public async Task RefreshAsync_FullRefresh_ExposesToolsInCatalogOrder()
    {
        var (state, fake, paths) = Build();
        SetupAll(fake, paths);

        await state.RefreshAsync();

        Assert.Equal(ToolCatalog.All.Select(t => t.Kind), state.Tools.Select(s => s.Kind));
        Assert.All(state.Tools, s => Assert.True(s.Found));
    }

    [Fact]
    public async Task IsAvailable_KeyedByToolKind_ReflectsDetection()
    {
        var (state, fake, paths) = Build();
        // Everything except uv present.
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(paths.Ytdlp, "2024.12.13");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");
        fake.Setup(paths.Deno, "deno 2.8.0 (...)");

        await state.RefreshAsync();

        Assert.False(state.IsAvailable(ToolKind.Uv));
        Assert.True(state.IsAvailable(ToolKind.AudioSeparator));
        Assert.True(state.IsAvailable(ToolKind.Ffmpeg));
    }

    [Fact]
    public async Task NamedProperties_BackedByToolKindLookup()
    {
        var (state, fake, paths) = Build();
        SetupAll(fake, paths);

        await state.RefreshAsync();

        Assert.True(state.IsUvAvailable);
        Assert.True(state.IsAudioSeparatorAvailable);
        Assert.True(state.IsYtdlpAvailable);
        Assert.True(state.IsFfmpegAvailable);
        Assert.True(state.IsDenoAvailable);
        Assert.True(state.CanDownloadFromUrl);
    }

    [Fact]
    public async Task CanDownloadFromUrl_RequiresYtdlpAndFfmpeg()
    {
        var (state, fake, paths) = Build();
        fake.Setup(paths.Uv, "uv 0.4.0");
        fake.Setup(paths.AudioSeparator, "audio-separator 0.27.2");
        fake.Setup(paths.Ffmpeg, "ffmpeg version 7.0");
        // yt-dlp missing → cannot download from URL.

        await state.RefreshAsync();

        Assert.True(state.IsFfmpegAvailable);
        Assert.False(state.IsYtdlpAvailable);
        Assert.False(state.CanDownloadFromUrl);
    }

    [Fact]
    public async Task RefreshAsync_Subset_OnlyUpdatesNamedKindsAndKeepsOthers()
    {
        var (state, fake, paths) = Build();
        // First pass: only uv present.
        fake.Setup(paths.Uv, "uv 0.4.0");
        await state.RefreshAsync();
        Assert.True(state.IsUvAvailable);
        Assert.False(state.IsYtdlpAvailable);

        // yt-dlp becomes available; refresh ONLY yt-dlp. uv must stay found.
        fake.Setup(paths.Ytdlp, "2024.12.13");
        await state.RefreshAsync(ToolKind.Ytdlp);

        Assert.True(state.IsYtdlpAvailable);
        Assert.True(state.IsUvAvailable);
        // The subset refresh must not have re-probed the others (no extra calls for ffmpeg etc).
        Assert.False(state.IsFfmpegAvailable);
    }

    [Fact]
    public async Task Tools_StateWrapsCatalogTool_NoDuplicatedMetadata()
    {
        var (state, fake, paths) = Build();
        SetupAll(fake, paths);

        await state.RefreshAsync();

        Assert.All(state.Tools, s => Assert.Same(ToolCatalog.Get(s.Kind), s.Tool));
        Assert.All(state.Tools, s => Assert.Equal(s.Tool.CliName, s.Name));
        Assert.All(state.Tools, s => Assert.Equal(s.Tool.IsRequired, s.IsRequired));
    }

    [Fact]
    public async Task RefreshAsync_SlowFullRefreshFinishingLast_DoesNotUndoAToolInstalledMeanwhile()
    {
        // The setup wizard's Install step starts a full refresh, and audio-separator's check is
        // slow. yt-dlp is installed and re-checked while it runs. The full refresh's older answer
        // for yt-dlp must not overwrite the newer one when it finally lands.
        var runner = new GatedRunner();
        var paths = new AppPaths(new AppSettings());
        var state = new ToolStateService(new SetupDetector(runner, paths));
        runner.Installed.Add(paths.AudioSeparator);
        await state.RefreshAsync(); // the launch-time detection
        runner.SlowChecks = true;

        var full = state.RefreshAsync();
        await runner.YtdlpCheckedByFullRefresh.Task;

        runner.Installed.Add(paths.Ytdlp);
        var partial = state.RefreshAsync(ToolKind.Ytdlp);
        runner.ReleaseSlowCheck.SetResult();
        await Task.WhenAll(full, partial);

        Assert.True(state.IsYtdlpAvailable);
    }

    // A tool is found when its path is in Installed. With SlowChecks on, audio-separator's check
    // waits for ReleaseSlowCheck, so a full refresh stays in flight until the test lets it finish.
    private sealed class GatedRunner : IProcessRunner
    {
        public HashSet<string> Installed { get; } = [];
        public TaskCompletionSource ReleaseSlowCheck { get; } = new();
        public TaskCompletionSource YtdlpCheckedByFullRefresh { get; } = new();
        public bool SlowChecks { get; set; }

        public async Task<ProcessRunner.Result> RunAsync(
            string exe,
            IEnumerable<string> args,
            bool logRawLines = true,
            CancellationToken ct = default
        )
        {
            var found = Installed.Contains(exe);
            if (SlowChecks && exe.Contains("audio-separator"))
                await ReleaseSlowCheck.Task;
            if (SlowChecks && exe.Contains("yt-dlp"))
                YtdlpCheckedByFullRefresh.TrySetResult();

            return found
                ? new ProcessRunner.Result(0, "1.0.0", string.Empty)
                : throw new System.ComponentModel.Win32Exception("not found");
        }

        public Task<ProcessRunner.Result> RunCheckedAsync(
            string exe,
            IEnumerable<string> args,
            bool logRawLines = true,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task RunStreamingAsync(
            string exe,
            IEnumerable<string> args,
            IProgress<string>? progress = null,
            bool logRawLines = true,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<ProcessRunner.Result> RunStreamingStderrAsync(
            string exe,
            IEnumerable<string> args,
            IProgress<string>? stderrProgress = null,
            bool logRawLines = true,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task RunPipedAsync(
            string producer,
            IEnumerable<string> producerArgs,
            string consumer,
            IEnumerable<string> consumerArgs,
            IProgress<string>? progress = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}

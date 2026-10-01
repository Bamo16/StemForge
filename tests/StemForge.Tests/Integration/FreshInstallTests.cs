using Microsoft.Extensions.DependencyInjection;

namespace StemForge.Tests.Integration;

/// <summary>
/// Installs ffmpeg, yt-dlp and deno the way setup does, into a temp bin folder, then uses them the
/// way StemForge does. Runs with STEMFORGE_LIVE_ASSETS=1; the YouTube download also needs
/// STEMFORGE_LIVE_YOUTUBE=1, since YouTube sometimes refuses CI machines.
/// </summary>
public sealed class FreshInstallTests(FreshInstallTests.Install install)
    : IClassFixture<FreshInstallTests.Install>
{
    // An NCS track (royalty-free), the one the README uses for its samples.
    private const string Video = "https://www.youtube.com/watch?v=G52OPfQUiZ0";

    private static readonly ToolKind[] BundledTools =
    [
        ToolKind.Ffmpeg,
        ToolKind.Ytdlp,
        ToolKind.Deno,
    ];

    public static bool LiveAssets => IsSet("STEMFORGE_LIVE_ASSETS");

    public static bool LiveYouTube => LiveAssets && IsSet("STEMFORGE_LIVE_YOUTUBE");

    [Fact(SkipUnless = nameof(LiveAssets), Skip = "Set STEMFORGE_LIVE_ASSETS=1 to run.")]
    public async Task Setup_InstallsEveryBundledTool_AndDetectionFindsIt()
    {
        var states = await install
            .Services.GetRequiredService<SetupDetector>()
            .DetectAsync(BundledTools);

        Assert.All(
            states,
            state => Assert.True(state.Found, $"{state.Name} not found after install")
        );
    }

    [Fact(
        SkipUnless = nameof(LiveYouTube),
        Skip = "Set STEMFORGE_LIVE_ASSETS=1 and STEMFORGE_LIVE_YOUTUBE=1 to run."
    )]
    public async Task UrlDownload_OnAFreshInstall_WritesTheAudio()
    {
        var ct = TestContext.Current.CancellationToken;
        var youTube = install.Services.GetRequiredService<YouTubeAudioService>();

        var meta = await youTube.ResolveAsync(Video, new AppSettings(), null, ct);
        var path = await youTube.DownloadAsync(meta, AudioFormat.Flac, install.Output, null, ct);

        Assert.True(new FileInfo(path).Length > 1_000_000, $"{path} is too small to be the song");
    }

    private static bool IsSet(string variable) =>
        Environment.GetEnvironmentVariable(variable) is "1" or "true" or "TRUE";

    /// <summary>One install shared by the class's tests, since ffmpeg alone is a large download.</summary>
    public sealed class Install : IAsyncLifetime
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"stemforge-fresh-{Guid.NewGuid():N}"
        );
        private ServiceProvider? _services;

        public IServiceProvider Services =>
            _services ?? throw new InvalidOperationException("The install did not run.");

        public string Output => Path.Combine(_root, "out");

        public async ValueTask InitializeAsync()
        {
            if (!LiveAssets)
                return;

            // The real service graph, with settings that override nothing and a bin folder of its own.
            var settings = new AppSettings();
            var services = new ServiceCollection().AddStemForgeCore();
            services.AddSingleton(settings);
            services.AddSingleton(
                new AppPaths(settings, bundledBinDir: Path.Combine(_root, "bin"))
            );
            _services = services.BuildServiceProvider();

            var fetcher = _services.GetRequiredService<BundledFetcher>();
            foreach (var kind in BundledTools)
                await fetcher.FetchAsync(
                    ToolCatalog.Get(kind),
                    ct: TestContext.Current.CancellationToken
                );
        }

        public async ValueTask DisposeAsync()
        {
            if (_services is not null)
                await _services.DisposeAsync();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}

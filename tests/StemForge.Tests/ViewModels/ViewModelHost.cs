using System.Reflection;
using StemForge.Services;
using StemForge.Tests.TestDoubles;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

/// <summary>
/// The faked collaborators the page view models are built from, all rooted in a temp folder so
/// nothing reads or writes the real user data. Commands that save <see cref="AppSettings"/> write
/// to the real settings file, so tests must not execute them.
/// </summary>
internal sealed class ViewModelHost : IDisposable
{
    public const string DemucsCatalog = """
        {
          "Demucs": {
            "Demucs v4: htdemucs_ft": {
              "filename": "htdemucs_ft.yaml",
              "stems": ["vocals", "drums", "bass", "other"],
              "download_files": ["https://example.test/htdemucs_ft.yaml"]
            }
          },
          "MDXC": {
            "Roformer X": { "filename": "x.ckpt", "stems": ["vocals", "other"], "download_files": ["x.ckpt"] }
          }
        }
        """;

    public ViewModelHost(AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        Settings.ModelsDirectory ??= Path.Combine(Root, "models");
        Settings.OutputDirectory ??= Path.Combine(Root, "out");
        Platform = PlatformInfo.Current;
        Paths = new AppPaths(Settings, Platform);
        Detector = new SetupDetector(Runner, Paths);
        ToolState = new ToolStateService(Detector);
    }

    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), $"stemforge-vm-{Guid.NewGuid():N}");

    public FakeProcessRunner Runner { get; } = new();
    public FakeSeparatorDriverService Driver { get; } = new();
    public AppSettings Settings { get; }
    public PlatformInfo Platform { get; }
    public AppPaths Paths { get; }
    public SetupDetector Detector { get; }
    public ToolStateService ToolState { get; }

    public UserPresetService UserPresets { get; } =
        new(Path.Combine(Path.GetTempPath(), $"stemforge-vm-presets-{Guid.NewGuid():N}.json"));

    public ModelCatalogService ModelCatalog => field ??= new ModelCatalogService(Runner, Paths);

    public SettingsViewModel CreateSettings(
        IPackageIndex? packages = null,
        string version = "0.2.0.0"
    ) =>
        new(
            Settings,
            Paths,
            Platform,
            new GpuDetector(Runner),
            new ToolInstaller(
                Runner,
                Paths,
                new BundledFetcher(Paths, Platform, NullFileDownloader.Instance),
                Platform
            ),
            ToolState,
            new DrumModelCatalog(ModelCatalog, new ModelProfileResolver()),
            packages ?? new StubPackageIndex(null),
            new AppInfo("StemForge", Version.Parse(version))
        );

    public SetupWizardViewModel CreateWizard() =>
        new(
            Settings,
            new GpuDetector(Runner),
            new ToolInstaller(
                Runner,
                Paths,
                new BundledFetcher(Paths, Platform, NullFileDownloader.Instance),
                Platform
            ),
            ToolState,
            Paths,
            Platform
        );

    public JobQueueService CreateJobQueue()
    {
        var youTube = new YouTubeAudioService(
            Runner,
            Paths,
            new YtdlpUpdater(Runner, Paths, TimeProvider.System)
        );
        var pipeline = new SeparationPipeline(
            Driver,
            youTube,
            new StubThumbnailFetcher(),
            Runner,
            Settings,
            Paths,
            AppInfo.Current
        );
        return new JobQueueService(pipeline, Settings);
    }

    public SeparateViewModel CreateSeparate(JobQueueService? queue = null) =>
        new(
            queue ?? CreateJobQueue(),
            Settings,
            UserPresets,
            ToolState,
            Paths,
            new YouTubeAudioService(
                Runner,
                Paths,
                new YtdlpUpdater(Runner, Paths, TimeProvider.System)
            ),
            new PresetCatalogService(Runner, Paths)
        );

    public MainWindowViewModel CreateMainWindow(
        SeparateViewModel? separate = null,
        QueueViewModel? queue = null,
        SettingsViewModel? settings = null,
        SetupWizardViewModel? wizard = null,
        string? latestReleaseTag = null
    )
    {
        var jobQueue = CreateJobQueue();
        return new MainWindowViewModel(
            separate ?? CreateSeparate(jobQueue),
            queue ?? new QueueViewModel(jobQueue),
            new ModelsViewModel(
                Paths,
                UserPresets,
                ModelCatalog,
                new ModelProfileResolver(),
                ToolState
            ),
            settings ?? CreateSettings(),
            wizard ?? CreateWizard(),
            new LogsViewModel(new AppLoggerSink()),
            Settings,
            new UpdateCheckService(
                new AppInfo("StemForge", Version.Parse("0.2.0.0")),
                new StubReleaseFetcher(latestReleaseTag)
            ),
            ToolState
        );
    }

    private sealed class StubReleaseFetcher(string? tag) : IReleaseFetcher
    {
        public Task<string?> FetchLatestTagAsync(CancellationToken ct = default) =>
            Task.FromResult(tag);
    }

    /// <summary>Raises a public <c>event Action</c> from outside, for events only a UI flow fires.</summary>
    public static void Raise(object owner, string eventName) =>
        (
            (Action?)
                owner
                    .GetType()
                    .GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(owner)
        )?.Invoke();

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    public sealed class StubPackageIndex(string? latest) : IPackageIndex
    {
        public Task<string?> LatestVersionAsync(string package, CancellationToken ct = default) =>
            Task.FromResult(latest);
    }

    /// <summary>Waits for a condition a fire-and-forget load will make true.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The view model never reached the expected state.");

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace StemForge.Core;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddStemForgeCore(this IServiceCollection services)
    {
        // Cross-cutting infrastructure
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton(AppSettings.Load());
        services.AddSingleton(UserPresetService.Load());
        services.AddSingleton(PlatformInfo.Current);
        services.AddSingleton<IAppInfo>(AppInfo.Current);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppPaths>();

        // HTTP clients
        services.ConfigureHttpClientDefaults(builder =>
            builder.WithUserAgent().WithTimeout(TimeSpan.FromSeconds(10))
        );
        services
            .AddHttpClient("github")
            .WithHeaders(new() { ["Accept"] = "application/vnd.github+json" });
        services.AddSingleton<IReleaseFetcher, GitHubReleaseFetcher>();
        services.AddHttpClient("thumbnail");
        services.AddSingleton<IThumbnailFetcher, ThumbnailFetcher>();
        services.AddHttpClient(
            ArtTrackFinder.HttpClientName,
            client =>
            {
                client.BaseAddress = new("https://music.youtube.com/");
                client.DefaultRequestHeaders.Add("Origin", "https://music.youtube.com");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0"
                );
            }
        );
        services.AddSingleton<IArtTrackFinder, ArtTrackFinder>();
        services.AddHttpClient("bundled").WithTimeout(TimeSpan.FromMinutes(15));
        services.AddSingleton<IFileDownloader, FileDownloader>();
        services.AddHttpClient("pypi");
        services.AddSingleton<IPackageIndex, PyPiPackageIndex>();
        services.AddHttpClient("model-config");
        services.AddSingleton<IModelConfigSource, ModelConfigSource>();

        // Update check
        services.AddSingleton<UpdateCheckService>();

        // Domain services
        services.AddSingleton<SetupDetector>();
        services.AddSingleton<GpuDetector>();
        services.AddSingleton<BundledFetcher>();
        services.AddSingleton<ToolInstaller>();
        services.AddSingleton<ToolReportService>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<ModelProfileResolver>();
        services.AddSingleton<DrumModelCatalog>();
        services.AddSingleton<PresetCatalogService>();
        services.AddSingleton<ToolStateService>();
        services.AddSingleton<YtdlpUpdater>();
        services.AddSingleton<YouTubeAudioService>();
        services.AddSingleton<ISeparatorDriverService, SeparatorDriverService>();
        services.AddSingleton<SeparationPipeline>();

        return services;
    }
}

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
        services.AddHttpClient("bundled").WithTimeout(TimeSpan.FromMinutes(15));
        services.AddSingleton<IFileDownloader, FileDownloader>();

        // Update check
        services.AddSingleton<UpdateCheckService>();

        // Domain services
        services.AddSingleton<SetupDetector>();
        services.AddSingleton<GpuDetector>();
        services.AddSingleton<BundledFetcher>();
        services.AddSingleton<ToolInstaller>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<ModelProfileResolver>();
        services.AddSingleton<DrumModelCatalog>();
        services.AddSingleton<PresetCatalogService>();
        services.AddSingleton<ToolStateService>();
        services.AddSingleton<YouTubeAudioService>();
        services.AddSingleton<ISeparatorDriverService, SeparatorDriverService>();
        services.AddSingleton<SeparationPipeline>();

        return services;
    }
}

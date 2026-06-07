using Microsoft.Extensions.DependencyInjection;
using StemForge.Extensions;
using StemForge.Models;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Composition root. Maps interfaces and concrete services to their lifetimes
/// so the rest of the app can request them via constructor injection.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

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
        services.AddSingleton<ToolStateService>();
        services.AddSingleton<YouTubeAudioService>();
        services.AddSingleton<ISeparatorDriverService, SeparatorDriverService>();
        services.AddSingleton<JobQueueService>();

        // View-models
        services.AddSingleton<SeparateViewModel>();
        services.AddSingleton<QueueViewModel>();
        services.AddSingleton<ModelsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SetupWizardViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}

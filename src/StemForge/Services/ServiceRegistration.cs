using Microsoft.Extensions.DependencyInjection;
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

        // Named HTTP clients
        services.AddHttpClient(
            "github",
            (sp, client) =>
            {
                var appInfo = sp.GetRequiredService<IAppInfo>();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"{appInfo.ProductName}/{appInfo.ShortVersion}"
                );
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.Timeout = TimeSpan.FromSeconds(10);
            }
        );
        services.AddHttpClient("thumbnail");
        services.AddHttpClient(
            "bundled",
            (sp, client) =>
            {
                var appInfo = sp.GetRequiredService<IAppInfo>();
                client.DefaultRequestHeaders.UserAgent.Add(
                    new(appInfo.ProductName, appInfo.ShortVersion)
                );
                client.Timeout = TimeSpan.FromMinutes(15);
            }
        );

        // Update check
        services.AddSingleton<IReleaseFetcher, GitHubReleaseFetcher>();
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

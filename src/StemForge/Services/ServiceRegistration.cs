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
        services.AddSingleton<AppPaths>();

        // Domain services
        services.AddSingleton<SetupDetector>();
        services.AddSingleton<GpuDetector>();
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

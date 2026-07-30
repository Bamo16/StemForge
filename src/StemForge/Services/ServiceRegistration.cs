using Microsoft.Extensions.DependencyInjection;
using StemForge.ViewModels;

namespace StemForge.Services;

/// <summary>
/// Composition root. Calls AddStemForgeCore for the shared service graph then adds
/// GUI-only registrations (AppLoggerSink, JobQueueService, and view-models).
/// </summary>
public static class ServiceRegistration
{
    public static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddStemForgeCore();

        // GUI-only infrastructure
        services.AddSingleton(sp => new AppLoggerSink(
            sp.GetRequiredService<AppSettings>().MaxLogEntries
        ));
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

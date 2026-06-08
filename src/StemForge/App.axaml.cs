using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.Services;
using StemForge.ViewModels;
using StemForge.Views;

namespace StemForge;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var provider = ServiceRegistration.BuildProvider();

        // Resolve the sink before Initialize so it is registered before the file opens.
        provider.GetRequiredService<AppLoggerSink>();
        AppLogger.Initialize(provider.GetRequiredService<AppSettings>().MaxLogEntries);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.Exit += (_, _) => AppLogger.Shutdown();
        }

        // Initial tool detection — was previously kicked off in the VM ctor.
        _ = provider.GetRequiredService<ToolStateService>().RefreshAsync();

        base.OnFrameworkInitializationCompleted();
    }
}

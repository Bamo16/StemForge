using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StemForge.Models;
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
        AppLogger.Initialize(AppSettings.Load().MaxLogEntries);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
            desktop.Exit += (_, _) => AppLogger.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

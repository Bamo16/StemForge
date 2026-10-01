using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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
        AppLogger.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.Exit += (_, _) => AppLogger.Shutdown();
        }

        // Initial tool detection — was previously kicked off in the VM ctor.
        var toolState = provider.GetRequiredService<ToolStateService>();
        _ = toolState.RefreshAsync();
        _ = UpdateYtdlpAsync(provider.GetRequiredService<YtdlpUpdater>(), toolState);

        base.OnFrameworkInitializationCompleted();
    }

    // Started at launch so a URL pasted later finds yt-dlp already current; a resolve waits for it
    // if it is still running. The refresh shows the new version in Settings.
    private static async Task UpdateYtdlpAsync(YtdlpUpdater updater, ToolStateService toolState)
    {
        if (await updater.EnsureCurrentAsync())
            await toolState.RefreshAsync(ToolKind.Ytdlp);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using StemForge.Extensions;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (
            DataContext is SettingsViewModel vm
            && vm.SettingsToolRows.All(r => !r.Found && string.IsNullOrEmpty(r.Version))
        )
            await vm.RefreshToolsCommand.ExecuteAsync(null);
    }

    private SettingsViewModel Vm => (SettingsViewModel)DataContext!;

    private async void OnBrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (await this.PickFolderAsync(Vm.OutputDirectory) is { } folder)
            Vm.OutputDirectory = folder;
    }

    private async void OnBrowseModelsClicked(object? sender, RoutedEventArgs e)
    {
        if (await this.PickFolderAsync(Vm.ModelsDirectory) is { } folder)
            Vm.ModelsDirectory = folder;
    }

    private async void OnBrowseToolPathClicked(object? sender, RoutedEventArgs e)
    {
        if (
            (sender as Control)?.DataContext is not SettingsToolRowViewModel row
            || await this.PickFilesAsync(suggestedStartPath: row.PathOverride) is not [{ } path, ..]
        )
            return;
        row.PathOverride = path;
    }
}

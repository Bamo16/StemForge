using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        if (DataContext is SettingsViewModel vm && vm.Tools.Count == 0)
            await vm.RefreshToolsCommand.ExecuteAsync(null);
    }

    private SettingsViewModel Vm => (SettingsViewModel)DataContext!;

    private async void OnBrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
            Vm.OutputDirectory = folder;
    }

    private async void OnBrowseModelsClicked(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
            Vm.ModelsDirectory = folder;
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false }
        );
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}

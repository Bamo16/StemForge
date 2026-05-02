using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class SetupWizardView : UserControl
{
    public SetupWizardView()
    {
        InitializeComponent();
    }

    private SetupWizardViewModel Vm => (SetupWizardViewModel)DataContext!;

    private async void OnBrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } folder)
            Vm.OutputDirectory = folder;
    }

    private async void OnBrowseModelsClicked(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync() is { } folder)
            Vm.ModelsDirectory = folder;
    }

    private async Task<string?> PickFolderAsync() =>
        TopLevel.GetTopLevel(this) is { StorageProvider: { } provider }
        && await provider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false }
        )
            is [{ Path.LocalPath: { } path }, ..]
            ? path
            : null;
}

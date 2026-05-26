using Avalonia.Controls;
using Avalonia.Interactivity;
using StemForge.Extensions;
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
        if (await this.PickFolderAsync(Vm.OutputDirectory) is { } folder)
            Vm.OutputDirectory = folder;
    }

    private async void OnBrowseModelsClicked(object? sender, RoutedEventArgs e)
    {
        if (await this.PickFolderAsync(Vm.ModelsDirectory) is { } folder)
            Vm.ModelsDirectory = folder;
    }
}

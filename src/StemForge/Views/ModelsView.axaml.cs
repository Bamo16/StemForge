using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StemForge.Views;

public partial class ModelsView : UserControl
{
    public ModelsView()
    {
        InitializeComponent();
    }

    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (
            sender is Button { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && TopLevel.GetTopLevel(this) is { } topLevel
        )
            await topLevel.Launcher.LaunchUriAsync(uri);
    }
}

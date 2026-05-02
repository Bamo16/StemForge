using CommunityToolkit.Mvvm.ComponentModel;

namespace StemForge.ViewModels;

public partial class NavItem : ObservableObject
{
    public required string Label { get; init; }

    /// <summary>Geometry path data for the Avalonia <c>Path</c> icon. 18×18 viewport.</summary>
    public required string IconData { get; init; }

    public required PageViewModelBase Target { get; init; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial int Badge { get; set; }
}

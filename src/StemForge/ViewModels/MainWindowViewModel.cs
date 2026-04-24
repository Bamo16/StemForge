using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StemForge.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // 18×18 icons. Minimal glyphs; refine later against the hi-fi mock.
    private const string IconSeparate = "M3 6 L15 6 M3 9 L15 9 M3 12 L15 12";
    private const string IconQueue = "M3 4 H15 V6 H3 Z M3 8 H15 V10 H3 Z M3 12 H15 V14 H3 Z";
    private const string IconModels = "M9 2 L15 5 V13 L9 16 L3 13 V5 Z M3 5 L9 8 L15 5 M9 8 V16";
    private const string IconSettings =
        "M9 6 A3 3 0 1 0 9 12 A3 3 0 1 0 9 6 M9 1 V3 M9 15 V17 M1 9 H3 M15 9 H17";

    [ObservableProperty]
    private PageViewModelBase _currentPage;

    public ObservableCollection<NavItem> NavItems { get; }

    public MainWindowViewModel()
    {
        var separate = new SeparateViewModel();
        var queue = new QueueViewModel();
        var models = new ModelsViewModel();
        var settings = new SettingsViewModel();

        NavItems =
        [
            new()
            {
                Label = "Separate",
                IconData = IconSeparate,
                Target = separate,
                IsActive = true,
            },
            new()
            {
                Label = "Queue",
                IconData = IconQueue,
                Target = queue,
            },
            new()
            {
                Label = "Models",
                IconData = IconModels,
                Target = models,
            },
            new()
            {
                Label = "Settings",
                IconData = IconSettings,
                Target = settings,
            },
        ];

        _currentPage = separate;
    }

    [RelayCommand]
    private void Navigate(NavItem item)
    {
        foreach (var n in NavItems)
        {
            n.IsActive = ReferenceEquals(n, item);
        }
        CurrentPage = item.Target;
    }
}

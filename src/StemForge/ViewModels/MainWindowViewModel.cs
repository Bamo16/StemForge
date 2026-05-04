using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // 18×18 icons — rough placeholders; refine against the hi-fi mock later.
    private const string IconSeparate = "M3 6 L15 6 M3 9 L15 9 M3 12 L15 12";
    private const string IconQueue = "M3 4 H15 V6 H3 Z M3 8 H15 V10 H3 Z M3 12 H15 V14 H3 Z";
    private const string IconModels = "M9 2 L15 5 V13 L9 16 L3 13 V5 Z M3 5 L9 8 L15 5 M9 8 V16";
    private const string IconSettings =
        "M9 6 A3 3 0 1 0 9 12 A3 3 0 1 0 9 6 M9 1 V3 M9 15 V17 M1 9 H3 M15 9 H17";
    private const string IconLogs = "M3 5 H5 M3 9 H5 M3 13 H5 M7 5 H15 M7 9 H15 M7 13 H13";

    [ObservableProperty]
    public partial PageViewModelBase CurrentPage { get; set; }
    public ObservableCollection<NavItem> NavItems { get; }

    public SetupWizardViewModel Wizard { get; }
    public LogsViewModel Logs { get; }
    public event Action? ShowLogsRequested;

    [ObservableProperty]
    public partial bool IsSetupRequired { get; set; }

    public MainWindowViewModel()
    {
        var appSettings = AppSettings.Load();
        var userPresets = UserPresetService.Load();
        var separation = new SeparationService(SetupDetector.ResolveAudioSeparatorPath());
        var queue = new JobQueueService(separation, appSettings);

        var separate = new SeparateViewModel(queue, appSettings, userPresets);
        var queueVm = new QueueViewModel(queue);
        var models = new ModelsViewModel(appSettings, userPresets);
        var settings = new SettingsViewModel(appSettings);

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
                Target = queueVm,
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

        Logs = new LogsViewModel();

        void GoToQueue()
        {
            var queueNav = NavItems.First(n => n.Label == "Queue");
            foreach (var n in NavItems)
                n.IsActive = ReferenceEquals(n, queueNav);
            CurrentPage = queueNav.Target;
        }

        separate.NavigateToQueueRequested += GoToQueue;
        CurrentPage = separate;

        Wizard = new SetupWizardViewModel(appSettings);
        IsSetupRequired = !appSettings.FirstRunComplete;
        Wizard.SetupCompleted += () => IsSetupRequired = false;
        Wizard.SetupDismissed += () => IsSetupRequired = false;
        settings.ShowWizardRequested += () =>
        {
            Wizard.Reset();
            IsSetupRequired = true;
        };
    }

    [RelayCommand]
    private void ShowLogs() => ShowLogsRequested?.Invoke();

    [RelayCommand]
    private void Navigate(NavItem item)
    {
        foreach (var n in NavItems)
            n.IsActive = ReferenceEquals(n, item);
        CurrentPage = item.Target;
    }
}

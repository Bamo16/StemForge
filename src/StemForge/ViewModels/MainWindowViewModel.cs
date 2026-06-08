using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Core.Models;
using StemForge.Core.Services;

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

    /// <summary>
    /// Non-null when an update is available. Contains the latest version string (e.g. "0.3.0").
    /// </summary>
    [ObservableProperty]
    public partial string? UpdateAvailableVersion { get; set; }

    /// <summary>URL of the GitHub Releases page, opened when the user clicks the update link.</summary>
    public string ReleasesUrl { get; } =
        $"https://github.com/{GitHubReleaseFetcher.RepoOwner}/{GitHubReleaseFetcher.RepoName}/releases";

    public MainWindowViewModel(
        SeparateViewModel separate,
        QueueViewModel queueVm,
        ModelsViewModel models,
        SettingsViewModel settings,
        SetupWizardViewModel wizard,
        LogsViewModel logs,
        AppSettings appSettings,
        UpdateCheckService updateCheckService
    )
    {
        Wizard = wizard;
        Logs = logs;

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

        CurrentPage = separate;
        IsSetupRequired = !appSettings.FirstRunComplete;

        separate.NavigateToQueueRequested += GoToQueue;
        Wizard.SetupCompleted += () =>
        {
            IsSetupRequired = false;
            separate.HasCompletedSetup = true;
        };
        Wizard.SetupDismissed += () =>
        {
            IsSetupRequired = false;
            // Dismiss leaves FirstRunComplete as-is; mirror that into SeparateViewModel so
            // the inline blocked-input messages only show after the user has at least
            // finished setup once.
            separate.HasCompletedSetup = appSettings.FirstRunComplete;
        };

        void OpenWizard()
        {
            Wizard.Reset();
            IsSetupRequired = true;
        }
        settings.ShowWizardRequested += OpenWizard;
        separate.ShowWizardRequested += OpenWizard;

        // Fire-and-forget: check for a newer release on startup. Failures are swallowed inside
        // UpdateCheckService, so this never blocks startup or raises an unhandled exception.
        _ = RunUpdateCheckAsync(updateCheckService);
    }

    private async Task RunUpdateCheckAsync(UpdateCheckService updateCheckService)
    {
        var result = await updateCheckService.CheckAsync().ConfigureAwait(false);
        if (result.UpdateAvailable)
            UpdateAvailableVersion = result.LatestVersion;
    }

    private void GoToQueue()
    {
        var queueNav = NavItems.First(n => n.Label == "Queue");
        foreach (var n in NavItems)
            n.IsActive = ReferenceEquals(n, queueNav);
        CurrentPage = queueNav.Target;
    }

    [RelayCommand]
    private void ShowLogs() => ShowLogsRequested?.Invoke();

    [RelayCommand]
    private static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }
        );

    [RelayCommand]
    private void Navigate(NavItem item)
    {
        foreach (var n in NavItems)
            n.IsActive = ReferenceEquals(n, item);
        CurrentPage = item.Target;
    }
}

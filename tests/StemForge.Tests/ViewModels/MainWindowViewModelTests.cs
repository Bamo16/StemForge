using Avalonia.Headless.XUnit;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

// FinishAsync on the wizard saves the real settings.json, so the "setup completed" path is
// triggered by raising the event instead of running that command.
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly ViewModelHost _host = new();

    public void Dispose() => _host.Dispose();

    private static NavItem Nav(MainWindowViewModel vm, string label) =>
        vm.NavItems.Single(item => item.Label == label);

    // ── Starting state ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void NavItems_AreSeparateQueueModelsSettingsInThatOrder()
    {
        var vm = _host.CreateMainWindow();

        Assert.Equal(
            ["Separate", "Queue", "Models", "Settings"],
            vm.NavItems.Select(item => item.Label)
        );
    }

    [AvaloniaFact]
    public void Startup_ShowsTheSeparatePageWithOnlyItsNavItemActive()
    {
        var vm = _host.CreateMainWindow();

        Assert.Same(Nav(vm, "Separate").Target, vm.CurrentPage);
        Assert.Equal(["Separate"], vm.NavItems.Where(item => item.IsActive).Select(i => i.Label));
    }

    [AvaloniaFact]
    public void NavItems_PointAtThePagesTheyAreNamedFor()
    {
        var queue = new QueueViewModel(_host.CreateJobQueue());
        var settings = _host.CreateSettings();

        var vm = _host.CreateMainWindow(queue: queue, settings: settings);

        Assert.Same(queue, Nav(vm, "Queue").Target);
        Assert.Same(settings, Nav(vm, "Settings").Target);
        Assert.IsType<ModelsViewModel>(Nav(vm, "Models").Target);
    }

    [AvaloniaFact]
    public void IsSetupRequired_FirstRun_IsTrue() =>
        Assert.True(_host.CreateMainWindow().IsSetupRequired);

    [AvaloniaFact]
    public void IsSetupRequired_SetupAlreadyDone_IsFalse()
    {
        _host.Settings.FirstRunComplete = true;

        Assert.False(_host.CreateMainWindow().IsSetupRequired);
    }

    [AvaloniaFact]
    public void ReleasesUrl_PointsAtTheProjectsReleasesPage() =>
        Assert.Equal(
            $"https://github.com/{GitHubReleaseFetcher.RepoOwner}/{GitHubReleaseFetcher.RepoName}/releases",
            _host.CreateMainWindow().ReleasesUrl
        );

    // ── Navigation ────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void NavigateCommand_ShowsThatPageAndMovesTheActiveMarker()
    {
        var vm = _host.CreateMainWindow();
        var models = Nav(vm, "Models");

        vm.NavigateCommand.Execute(models);

        Assert.Same(models.Target, vm.CurrentPage);
        Assert.Equal(["Models"], vm.NavItems.Where(item => item.IsActive).Select(i => i.Label));
    }

    [AvaloniaFact]
    public void NavigateCommand_BackToSeparate_ClearsTheOtherMarker()
    {
        var vm = _host.CreateMainWindow();
        vm.NavigateCommand.Execute(Nav(vm, "Settings"));

        vm.NavigateCommand.Execute(Nav(vm, "Separate"));

        Assert.Equal(["Separate"], vm.NavItems.Where(item => item.IsActive).Select(i => i.Label));
    }

    [AvaloniaFact]
    public void QueueingARun_JumpsToTheQueuePage()
    {
        var separate = _host.CreateSeparate();
        var vm = _host.CreateMainWindow(separate);

        ViewModelHost.Raise(separate, nameof(SeparateViewModel.NavigateToQueueRequested));

        Assert.Same(Nav(vm, "Queue").Target, vm.CurrentPage);
        Assert.Equal(["Queue"], vm.NavItems.Where(item => item.IsActive).Select(i => i.Label));
    }

    // ── Setup wizard ──────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DismissingTheWizard_ClosesItButDoesNotMarkSetupComplete()
    {
        var separate = _host.CreateSeparate();
        var vm = _host.CreateMainWindow(separate);

        vm.Wizard.DismissCommand.Execute(null);

        Assert.False(vm.IsSetupRequired);
        Assert.False(separate.HasCompletedSetup);
    }

    [AvaloniaFact]
    public void DismissingTheWizard_AfterAnEarlierCompletedSetup_KeepsSeparateInThatState()
    {
        _host.Settings.FirstRunComplete = true;
        var separate = _host.CreateSeparate();
        var vm = _host.CreateMainWindow(separate);
        separate.HasCompletedSetup = false;

        vm.Wizard.DismissCommand.Execute(null);

        Assert.True(separate.HasCompletedSetup);
    }

    [AvaloniaFact]
    public void CompletingSetup_ClosesTheWizardAndTellsSeparateSetupIsDone()
    {
        var separate = _host.CreateSeparate();
        var vm = _host.CreateMainWindow(separate);

        ViewModelHost.Raise(vm.Wizard, nameof(SetupWizardViewModel.SetupCompleted));

        Assert.False(vm.IsSetupRequired);
        Assert.True(separate.HasCompletedSetup);
    }

    [AvaloniaFact]
    public async Task SettingsShowWizard_ReopensTheWizardFromTheWelcomeStep()
    {
        _host.Settings.FirstRunComplete = true;
        var settings = _host.CreateSettings();
        var vm = _host.CreateMainWindow(settings: settings);
        // The first detection reopens the wizard itself (tools are missing), so let it happen first.
        await _host.ToolState.RefreshAsync();
        vm.Wizard.CurrentStep = WizardStep.Install;
        Assert.NotEqual(WizardStep.Welcome, vm.Wizard.CurrentStep);

        settings.ShowWizardCommand.Execute(null);

        Assert.True(vm.IsSetupRequired);
        Assert.Equal(WizardStep.Welcome, vm.Wizard.CurrentStep);
        Assert.True(vm.Wizard.CanDismiss);
    }

    [AvaloniaFact]
    public void SeparateShowWizard_ReopensTheWizard()
    {
        _host.Settings.FirstRunComplete = true;
        var separate = _host.CreateSeparate();
        var vm = _host.CreateMainWindow(separate);

        separate.ShowWizardCommand.Execute(null);

        Assert.True(vm.IsSetupRequired);
    }

    // ── First tool detection after launch ─────────────────────────────────────

    [AvaloniaFact]
    public async Task FirstDetection_RequiredToolMissingAfterSetup_ReopensTheWizard()
    {
        _host.Settings.FirstRunComplete = true;
        var vm = _host.CreateMainWindow();
        Assert.False(vm.IsSetupRequired);

        await _host.ToolState.RefreshAsync();

        Assert.True(vm.IsSetupRequired);
    }

    [AvaloniaFact]
    public async Task FirstDetection_OnlyActsOnce_SoDismissingAfterwardsSticks()
    {
        _host.Settings.FirstRunComplete = true;
        var vm = _host.CreateMainWindow();
        await _host.ToolState.RefreshAsync();
        Assert.True(vm.IsSetupRequired);

        vm.Wizard.DismissCommand.Execute(null);
        await _host.ToolState.RefreshAsync();

        Assert.False(vm.IsSetupRequired);
    }

    // ── Update banner ─────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task UpdateAvailableVersion_NewerRelease_ShowsItsVersion()
    {
        var vm = _host.CreateMainWindow(latestReleaseTag: "v9.0.0");

        await ViewModelHost.WaitUntilAsync(() => vm.UpdateAvailableVersion is not null);

        Assert.Equal("9.0.0", vm.UpdateAvailableVersion);
    }

    [AvaloniaFact]
    public async Task UpdateAvailableVersion_NoNewerRelease_StaysNull()
    {
        var vm = _host.CreateMainWindow(latestReleaseTag: "v0.1.0");

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Null(vm.UpdateAvailableVersion);
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ShowLogsCommand_AsksTheWindowToShowTheLogs()
    {
        var vm = _host.CreateMainWindow();
        var requested = 0;
        vm.ShowLogsRequested += () => requested++;

        vm.ShowLogsCommand.Execute(null);

        Assert.Equal(1, requested);
    }
}

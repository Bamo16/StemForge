using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

// Commands that save AppSettings (Save, ConfirmChangeVariant, ConfirmReinstall, ConfirmUninstall,
// UpgradeAudioSeparator) write the real settings.json, so none is executed here.
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly ViewModelHost _host = new();

    public void Dispose() => _host.Dispose();

    // ── Footer ────────────────────────────────────────────────────────────────

    [Fact]
    public void VersionText_IsTheFullVersionWithAPrefix() =>
        Assert.Equal("v0.2.1.1", _host.CreateSettings(version: "0.2.1.1").VersionText);

    [Fact]
    public void ProductName_ComesFromTheAppInfo() =>
        Assert.Equal("StemForge", _host.CreateSettings().ProductName);

    // ── Loading from the saved settings ───────────────────────────────────────

    [Fact]
    public void Constructor_ShowsSavedSettingsInTheirFields()
    {
        var settings = new AppSettings
        {
            OutputDirectory = @"D:\out",
            ModelsDirectory = @"D:\models",
            YtdlpCookiesFromBrowser = "firefox",
            DefaultAudioFormat = AudioFormat.Mp3,
            GpuVariant = GpuVariant.Cuda,
            DrumExtractionModel = "my.ckpt",
            DrumStemLocation = DrumStemLocation.CacheOnly,
        };
        using var host = new ViewModelHost(settings);

        var vm = host.CreateSettings();

        Assert.Equal(@"D:\out", vm.OutputDirectory);
        Assert.Equal(@"D:\models", vm.ModelsDirectory);
        Assert.Equal("firefox", vm.YtdlpCookiesFromBrowser);
        Assert.Equal(AudioFormat.Mp3, vm.DefaultAudioFormat);
        Assert.Equal(GpuVariant.Cuda, vm.GpuVariant);
        Assert.Equal("my.ckpt", vm.DrumExtractionModel);
        Assert.False(vm.DrumStemsWithOutputs);
    }

    [Fact]
    public void Constructor_UnsetDirectoriesShowAsEmptyText()
    {
        using var host = new ViewModelHost(new AppSettings());
        host.Settings.OutputDirectory = null;
        host.Settings.ModelsDirectory = null;

        var vm = host.CreateSettings();

        Assert.Equal(string.Empty, vm.OutputDirectory);
        Assert.Equal(string.Empty, vm.ModelsDirectory);
    }

    [Fact]
    public void ToolPathsSection_StartsCollapsedWhenNoOverrideIsSet() =>
        Assert.False(_host.CreateSettings().IsToolPathsExpanded);

    [Fact]
    public void ToolPathsSection_StartsExpandedWhenAnOverrideIsActive()
    {
        _host.Settings.SetToolPathOverride(ToolKind.Ffmpeg, @"C:\tools\ffmpeg.exe");

        var vm = _host.CreateSettings();

        Assert.True(vm.IsToolPathsExpanded);
        Assert.Equal(
            @"C:\tools\ffmpeg.exe",
            vm.SettingsToolRows.Single(row => row.Kind == ToolKind.Ffmpeg).PathOverride
        );
    }

    [Fact]
    public void ToggleToolPathsCommand_FlipsTheSection()
    {
        var vm = _host.CreateSettings();

        vm.ToggleToolPathsCommand.Execute(null);
        Assert.True(vm.IsToolPathsExpanded);

        vm.ToggleToolPathsCommand.Execute(null);
        Assert.False(vm.IsToolPathsExpanded);
    }

    [Fact]
    public void SettingsToolRows_ListEveryCatalogTool() =>
        Assert.Equal(
            ToolCatalog.All.Select(tool => tool.Kind),
            _host.CreateSettings().SettingsToolRows.Select(row => row.Kind)
        );

    // ── GPU variant ───────────────────────────────────────────────────────────

    [Fact]
    public void SetGpuVariantCommand_SelectsTheNamedVariantAndItsFlag()
    {
        var vm = _host.CreateSettings();

        vm.SetGpuVariantCommand.Execute("DirectML");

        Assert.Equal(GpuVariant.DirectML, vm.GpuVariant);
        Assert.True(vm.IsDirectML);
        Assert.False(vm.IsCpu);
        Assert.False(vm.IsCuda);
    }

    // ── audio-separator version line ──────────────────────────────────────────

    [Theory]
    [InlineData("0.9.0", "0.10.0", true)] // compared as versions, not as text
    [InlineData("0.10.0", "0.9.0", false)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.3", null, false)]
    [InlineData("", "1.2.3", false)]
    [InlineData("not-a-version", "1.2.3", false)]
    public void IsAudioSeparatorUpdateAvailable_ComparesInstalledAgainstLatest(
        string installed,
        string? latest,
        bool expected
    )
    {
        var vm = _host.CreateSettings();

        vm.AudioSeparatorVersion = installed;
        vm.LatestAudioSeparatorVersion = latest;

        Assert.Equal(expected, vm.IsAudioSeparatorUpdateAvailable);
    }

    [Fact]
    public void AudioSeparatorVersionText_NotInstalled_IsEmpty()
    {
        var vm = _host.CreateSettings();

        vm.AudioSeparatorVersion = string.Empty;
        vm.LatestAudioSeparatorVersion = "9.9.9";

        Assert.Equal(string.Empty, vm.AudioSeparatorVersionText);
    }

    [Fact]
    public void AudioSeparatorVersionText_UpToDate_OnlyNamesTheInstalledVersion()
    {
        var vm = _host.CreateSettings();

        vm.AudioSeparatorVersion = "0.30.1";
        vm.LatestAudioSeparatorVersion = "0.30.1";

        Assert.Equal("Version 0.30.1 installed.", vm.AudioSeparatorVersionText);
    }

    [Fact]
    public void AudioSeparatorVersionText_NewerAvailable_NamesBothVersions()
    {
        var vm = _host.CreateSettings();

        vm.AudioSeparatorVersion = "0.30.1";
        vm.LatestAudioSeparatorVersion = "0.31.0";

        Assert.Equal(
            "Version 0.30.1 installed. 0.31.0 is available.",
            vm.AudioSeparatorVersionText
        );
    }

    [Fact]
    public async Task LatestAudioSeparatorVersion_IsFilledFromThePackageIndex()
    {
        var vm = _host.CreateSettings(new ViewModelHost.StubPackageIndex("0.31.0"));

        await ViewModelHost.WaitUntilAsync(() => vm.LatestAudioSeparatorVersion is not null);

        Assert.Equal("0.31.0", vm.LatestAudioSeparatorVersion);
    }

    [Theory]
    [InlineData(GpuVariant.Cuda, "CUDA")]
    [InlineData(GpuVariant.DirectML, "DirectML")]
    [InlineData(GpuVariant.Cpu, "CPU")]
    public void InstalledVariantLabel_NamesTheInstalledVariant(GpuVariant variant, string label)
    {
        _host.Settings.InstalledVariant = variant;

        Assert.Equal(label, _host.CreateSettings().InstalledVariantLabel);
    }

    [Fact]
    public void InstalledVariantLabel_NothingRecorded_SaysUnknown() =>
        Assert.Equal("Unknown", _host.CreateSettings().InstalledVariantLabel);

    // ── Change-variant / reinstall / uninstall panels ─────────────────────────

    [Fact]
    public void BeginChangeVariantCommand_OpensThePanelPreselectingTheInstalledVariant()
    {
        _host.Settings.InstalledVariant = GpuVariant.Cuda;
        var vm = _host.CreateSettings();

        vm.BeginChangeVariantCommand.Execute(null);

        Assert.True(vm.IsChangeVariantOpen);
        Assert.Equal(GpuVariant.Cuda, vm.PendingVariant);
        Assert.True(vm.IsPendingCuda);
    }

    [Fact]
    public void BeginChangeVariantCommand_NothingInstalled_PreselectsTheChosenGpuVariant()
    {
        var vm = _host.CreateSettings();
        vm.SetGpuVariantCommand.Execute("DirectML");

        vm.BeginChangeVariantCommand.Execute(null);

        Assert.Equal(GpuVariant.DirectML, vm.PendingVariant);
    }

    [Fact]
    public void ConfirmChangeVariantCommand_CannotRunWhileThePendingVariantIsTheInstalledOne()
    {
        _host.Settings.InstalledVariant = GpuVariant.Cpu;
        var vm = _host.CreateSettings();
        vm.BeginChangeVariantCommand.Execute(null);

        Assert.False(vm.ConfirmChangeVariantCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmChangeVariantCommand_CanRunOncePendingDiffersFromInstalled()
    {
        _host.Settings.InstalledVariant = GpuVariant.Cpu;
        var vm = _host.CreateSettings();
        vm.BeginChangeVariantCommand.Execute(null);

        vm.SetPendingVariantCommand.Execute("Cuda");

        Assert.True(vm.ConfirmChangeVariantCommand.CanExecute(null));
        Assert.False(vm.IsPendingCpu);
    }

    [Fact]
    public void ConfirmChangeVariantCommand_CanRunAgainstAnUnrecordedInstall()
    {
        var vm = _host.CreateSettings();
        vm.BeginChangeVariantCommand.Execute(null);

        Assert.True(vm.ConfirmChangeVariantCommand.CanExecute(null));
    }

    [Fact]
    public void Panels_OpeningOneClosesTheOthers()
    {
        var vm = _host.CreateSettings();

        vm.BeginChangeVariantCommand.Execute(null);
        vm.BeginReinstallCommand.Execute(null);

        Assert.False(vm.IsChangeVariantOpen);
        Assert.True(vm.IsReinstallConfirmOpen);

        vm.BeginUninstallCommand.Execute(null);

        Assert.False(vm.IsReinstallConfirmOpen);
        Assert.True(vm.IsUninstallConfirmOpen);
    }

    [Fact]
    public void CancelManageCommand_ClosesEveryPanelAndClearsTheError()
    {
        var vm = _host.CreateSettings();
        vm.BeginUninstallCommand.Execute(null);
        vm.ManageError = "boom";

        vm.CancelManageCommand.Execute(null);

        Assert.False(vm.IsUninstallConfirmOpen);
        Assert.Null(vm.ManageError);
    }

    [Fact]
    public void BeginningAnotherPanel_ClearsALeftOverError()
    {
        var vm = _host.CreateSettings();
        vm.ManageError = "boom";

        vm.BeginReinstallCommand.Execute(null);

        Assert.Null(vm.ManageError);
    }

    // ── Wizard hand-off ───────────────────────────────────────────────────────

    [Fact]
    public void ShowWizardCommand_AsksTheHostToOpenTheWizard()
    {
        var vm = _host.CreateSettings();
        var requested = 0;
        vm.ShowWizardRequested += () => requested++;

        vm.ShowWizardCommand.Execute(null);

        Assert.Equal(1, requested);
    }

    // ── Drum model picker ─────────────────────────────────────────────────────

    private SettingsViewModel CreateWithDrumCatalog()
    {
        _host.Runner.Setup(_host.Paths.SeparationDriverPython, ViewModelHost.DemucsCatalog);
        return _host.CreateSettings();
    }

    [Fact]
    public async Task DrumModelOptions_ListOnlyModelsThatEmitDrums()
    {
        var vm = CreateWithDrumCatalog();

        await ViewModelHost.WaitUntilAsync(() => !vm.DrumModelsLoading);

        Assert.Equal(["htdemucs_ft.yaml"], vm.DrumModelOptions.Select(option => option.Filename));
    }

    [Fact]
    public async Task SavedDrumModelInTheList_IsSelectedAndNotFlaggedAsUnlisted()
    {
        _host.Settings.DrumExtractionModel = "HTDEMUCS_FT.YAML"; // match ignores case
        var vm = CreateWithDrumCatalog();

        await ViewModelHost.WaitUntilAsync(() => !vm.DrumModelsLoading);

        Assert.Equal("htdemucs_ft.yaml", vm.SelectedDrumModel?.Filename);
        Assert.False(vm.HasUnlistedDrumModel);
    }

    [Fact]
    public async Task SavedDrumModelMissingFromTheList_IsKeptAndFlaggedAsUnlisted()
    {
        _host.Settings.DrumExtractionModel = "removed_model.ckpt";
        var vm = CreateWithDrumCatalog();

        await ViewModelHost.WaitUntilAsync(() => !vm.DrumModelsLoading);

        Assert.Null(vm.SelectedDrumModel);
        Assert.Equal("removed_model.ckpt", vm.DrumExtractionModel);
        Assert.True(vm.HasUnlistedDrumModel);
    }

    [Fact]
    public void HasUnlistedDrumModel_WhileTheListIsStillLoading_IsFalse()
    {
        var vm = _host.CreateSettings();
        vm.DrumModelsLoading = true;
        vm.DrumExtractionModel = "anything.ckpt";

        Assert.False(vm.HasUnlistedDrumModel);
    }

    [Fact]
    public async Task PickingADrumModel_WritesItsFilenameBackAndClearsTheUnlistedFlag()
    {
        _host.Settings.DrumExtractionModel = "removed_model.ckpt";
        var vm = CreateWithDrumCatalog();
        await ViewModelHost.WaitUntilAsync(() => !vm.DrumModelsLoading);

        vm.SelectedDrumModel = vm.DrumModelOptions.Single();

        Assert.Equal("htdemucs_ft.yaml", vm.DrumExtractionModel);
        Assert.False(vm.HasUnlistedDrumModel);
    }

    [Fact]
    public async Task ClearingTheDrumPick_KeepsTheStoredFilename()
    {
        var vm = CreateWithDrumCatalog();
        await ViewModelHost.WaitUntilAsync(() => !vm.DrumModelsLoading);

        vm.SelectedDrumModel = null;

        Assert.Equal("htdemucs_ft.yaml", vm.DrumExtractionModel);
    }

    // ── Tool detection ────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterDetection_WithRequiredToolsMissing_NotAllSystemsAreGo()
    {
        var vm = _host.CreateSettings();

        await _host.ToolState.RefreshAsync();
        await ViewModelHost.WaitUntilAsync(() => !vm.ToolsLoading);

        Assert.False(vm.AllSystemsGo);
        Assert.All(vm.SettingsToolRows, row => Assert.False(row.Found));
        Assert.False(vm.IsAudioSeparatorInstalled);
    }
}

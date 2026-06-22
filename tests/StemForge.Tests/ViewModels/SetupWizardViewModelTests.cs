using StemForge.Tests.Fakes;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class SetupWizardViewModelTests
{
    private static SetupWizardViewModel Build(FakeProcessRunner? fake = null) => Build(out _, fake);

    private static SetupWizardViewModel Build(out AppPaths paths, FakeProcessRunner? fake = null)
    {
        fake ??= new FakeProcessRunner();
        var settings = new AppSettings();
        paths = new AppPaths(settings);
        var runner = (IProcessRunner)fake;
        var setupDetector = new SetupDetector(runner, paths);
        var platform = PlatformInfo.Current;
        var bundledFetcher = new BundledFetcher(paths, platform, NullFileDownloader.Instance);
        return new SetupWizardViewModel(
            settings,
            setupDetector,
            new GpuDetector(runner),
            new ToolInstaller(runner, paths, bundledFetcher, platform),
            new ToolStateService(setupDetector),
            paths,
            platform
        );
    }

    private static ToolRowViewModel Row(SetupWizardViewModel vm, ToolKind kind) =>
        vm.InstallRows.Single(r => r.Kind == kind);

    [Fact]
    public void InitialStep_IsWelcome()
    {
        var vm = Build();
        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
        Assert.True(vm.IsWelcomeStep);
    }

    [Fact]
    public void Start_MovesToDetect()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("uv", "uv 0.4.0");
        fake.Setup("audio-separator", "audio-separator 0.27.2");

        var vm = Build(fake);
        vm.StartCommand.Execute(null);

        Assert.Equal(WizardStep.Detect, vm.CurrentStep);
        Assert.True(vm.IsDetectStep);
        Assert.False(vm.IsWelcomeStep);
    }

    [Fact]
    public void Back_FromDetect_GoesToWelcome()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Detect;
        vm.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
    }

    [Fact]
    public void Next_FromDirectories_GoesToInstall()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Directories;
        vm.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Install, vm.CurrentStep);
    }

    [Fact]
    public void Next_FromInstall_GoesToFinish()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Install;
        vm.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Finish, vm.CurrentStep);
    }

    [Fact]
    public void CanGoNext_OnInstallStep_RequiresAllRequiredToolsFound()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Install; // EnsureInstallRows populates rows synchronously

        // All required tools missing
        Assert.False(vm.CanGoNext);

        // Only audio-separator (need uv and ffmpeg too)
        Row(vm, ToolKind.AudioSeparator).Found = true;
        Assert.False(vm.CanGoNext);

        Row(vm, ToolKind.Uv).Found = true;
        Assert.False(vm.CanGoNext);

        Row(vm, ToolKind.Ffmpeg).Found = true;
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void CanGoNext_OnWelcomeStep_AlwaysTrue()
    {
        var vm = Build();
        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void IsBackVisible_OnlyForMiddleSteps()
    {
        var vm = Build();

        vm.CurrentStep = WizardStep.Welcome;
        Assert.False(vm.IsBackVisible);

        vm.CurrentStep = WizardStep.Detect;
        Assert.True(vm.IsBackVisible);

        vm.CurrentStep = WizardStep.Directories;
        Assert.True(vm.IsBackVisible);

        vm.CurrentStep = WizardStep.Install;
        Assert.True(vm.IsBackVisible);

        vm.CurrentStep = WizardStep.Finish;
        Assert.False(vm.IsBackVisible);
    }

    [Fact]
    public void StepIndicators_ActivateProgressively()
    {
        var vm = Build();

        vm.CurrentStep = WizardStep.Welcome;
        Assert.False(vm.IsStep1Active);

        vm.CurrentStep = WizardStep.Detect;
        Assert.True(vm.IsStep1Active);
        Assert.False(vm.IsStep2Active);

        vm.CurrentStep = WizardStep.Directories;
        Assert.True(vm.IsStep1Active);
        Assert.True(vm.IsStep2Active);
        Assert.False(vm.IsStep3Active);

        vm.CurrentStep = WizardStep.Install;
        Assert.True(vm.IsStep3Active);
        Assert.False(vm.IsStep4Active);

        vm.CurrentStep = WizardStep.Finish;
        Assert.True(vm.IsStep4Active);
    }

    [Fact]
    public void Reset_RestoresWelcomeStep()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Install;
        Row(vm, ToolKind.AudioSeparator).Found = true;

        vm.Reset();

        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
        Assert.Empty(vm.InstallRows);
        Assert.True(vm.CanDismiss);
    }

    [Fact]
    public void SetupDismissed_FiredByDismissCommand()
    {
        var vm = Build();
        bool fired = false;
        vm.SetupDismissed += () => fired = true;

        vm.DismissCommand.Execute(null);

        Assert.True(fired);
    }

    [Fact]
    public void GpuVariantHelpers_ReflectCurrentVariant()
    {
        var vm = Build();

        vm.SetGpuVariantCommand.Execute("Cpu");
        Assert.True(vm.IsCpu);
        Assert.False(vm.IsCuda);
        Assert.False(vm.IsDirectML);

        vm.SetGpuVariantCommand.Execute("Cuda");
        Assert.False(vm.IsCpu);
        Assert.True(vm.IsCuda);

        vm.SetGpuVariantCommand.Execute("DirectML");
        Assert.True(vm.IsDirectML);
    }

    // Plain [Fact]: FakeProcessRunner returns Task.FromResult everywhere, so every await in
    // InstallSelectedAsync completes inline (no SynchronizationContext → no dispatcher queue).
    // [AvaloniaFact] was actively harmful here: the Avalonia SynchronizationContext causes
    // await-on-completed-task to *post* continuations rather than run them inline, which lets
    // the fire-and-forget RecheckToolsAsync continuation race against the test's WantInstall
    // assignments and produce non-deterministic results.
    [Fact]
    public async Task InstallSelected_SetsIndeterminateProgressDuringAudioSeparatorInstall()
    {
        // Resolve the actual exe paths the wizard will probe/install with (uv resolves to a known
        // absolute path when present on the host, not the bare "uv"), and key the fake off those.
        var fake = new FakeProcessRunner();
        var vm = Build(out var paths, fake);
        // uv must install and detect so the audio-separator gate (_toolState.IsAvailable(uv)) passes.
        // The uv ScriptInstall runs powershell on Windows / sh on Unix; set up both so this stays
        // deterministic regardless of host or the wizard's fire-and-forget recheck. The
        // audio-separator uv-tool install then runs but its post-install detection finds nothing,
        // so the row ends in an error -- exercising the failure-clear path (state must clear).
        fake.Setup(paths.Uv, "uv 0.4.0");
        fake.Setup("powershell", "");
        fake.Setup("sh", "");

        vm.CurrentStep = WizardStep.Install; // EnsureInstallRows populates rows synchronously

        var uvRow = Row(vm, ToolKind.Uv);
        uvRow.WantInstall = true;
        uvRow.Found = false;

        var asRow = Row(vm, ToolKind.AudioSeparator);
        asRow.WantInstall = true;
        asRow.Found = false;

        // Capture the in-progress state at the moment InProgressMessage is set.
        string? messageWhileInstalling = null;
        var wasInstalling = false;
        asRow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolRowViewModel.IsInstalling) && asRow.IsInstalling)
                wasInstalling = true;
            if (
                e.PropertyName == nameof(ToolRowViewModel.InProgressMessage)
                && asRow.InProgressMessage.Length > 0
            )
                messageWhileInstalling = asRow.InProgressMessage;
        };

        await vm.InstallSelectedCommand.ExecuteAsync(null);

        Assert.True(
            wasInstalling,
            $"IsInstalling never set. calls=[{string.Join(",", fake.Calls.Select(c => c.Exe))}] found={asRow.Found} want={asRow.WantInstall} err={asRow.InstallError}"
        );
        Assert.NotNull(messageWhileInstalling);
        Assert.Contains("several minutes", messageWhileInstalling);

        // State must clear after the install completes (here, on failure).
        Assert.False(asRow.IsInstalling);
        Assert.Equal(string.Empty, asRow.InProgressMessage);
        Assert.False(vm.IsInstalling);
    }

    [Fact]
    public void VariantPicker_OffersOnlyCurrentOsVariants()
    {
        IVariantPicker vm = Build();

        var install = (UvToolInstall)ToolCatalog.Get(ToolKind.AudioSeparator).InstallStrategy;
        var expected = install.VariantsFor(PlatformInfo.Current.Os).Select(v => v.Variant).ToList();

        Assert.Equal(expected.Contains(GpuVariant.Cpu), vm.HasCpuVariant);
        Assert.Equal(expected.Contains(GpuVariant.Cuda), vm.HasCudaVariant);
        Assert.Equal(expected.Contains(GpuVariant.DirectML), vm.HasDirectMLVariant);
    }
}

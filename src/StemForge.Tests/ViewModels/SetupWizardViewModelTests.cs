using Avalonia.Headless.XUnit;
using StemForge.Models;
using StemForge.Services;
using StemForge.Tests.Fakes;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class SetupWizardViewModelTests
{
    private static SetupWizardViewModel Build(FakeProcessRunner? fake = null)
    {
        fake ??= new FakeProcessRunner();
        var settings = new AppSettings();
        var paths = new AppPaths(settings);
        var runner = (IProcessRunner)fake;
        var setupDetector = new SetupDetector(runner, paths);
        return new SetupWizardViewModel(
            settings,
            setupDetector,
            new GpuDetector(runner),
            new ToolInstaller(runner, paths),
            new FfmpegFetcher(paths),
            new ToolStateService(setupDetector),
            paths
        );
    }

    [AvaloniaFact]
    public void InitialStep_IsWelcome()
    {
        var vm = Build();
        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
        Assert.True(vm.IsWelcomeStep);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void Back_FromDetect_GoesToWelcome()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Detect;
        vm.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
    }

    [AvaloniaFact]
    public void Next_FromDirectories_GoesToInstall()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Directories;
        vm.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Install, vm.CurrentStep);
    }

    [AvaloniaFact]
    public void Next_FromInstall_GoesToFinish()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Install;
        vm.AudioSeparatorFound = true;
        vm.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Finish, vm.CurrentStep);
    }

    [AvaloniaFact]
    public void CanGoNext_OnInstallStep_RequiresAudioSeparatorFound()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Install;

        vm.AudioSeparatorFound = false;
        Assert.False(vm.CanGoNext);

        vm.AudioSeparatorFound = true;
        Assert.True(vm.CanGoNext);
    }

    [AvaloniaFact]
    public void CanGoNext_OnWelcomeStep_AlwaysTrue()
    {
        var vm = Build();
        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
        Assert.True(vm.CanGoNext);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void Reset_RestoresWelcomeStep()
    {
        var vm = Build();
        vm.CurrentStep = WizardStep.Finish;
        vm.AudioSeparatorFound = true;

        vm.Reset();

        Assert.Equal(WizardStep.Welcome, vm.CurrentStep);
        Assert.False(vm.AudioSeparatorFound);
        Assert.True(vm.CanDismiss);
    }

    [AvaloniaFact]
    public void SetupDismissed_FiredByDismissCommand()
    {
        var vm = Build();
        bool fired = false;
        vm.SetupDismissed += () => fired = true;

        vm.DismissCommand.Execute(null);

        Assert.True(fired);
    }

    [AvaloniaFact]
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
}

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public enum WizardStep
{
    Welcome,
    Detect,
    Directories,
    Install,
    Finish,
}

public partial class SetupWizardViewModel(AppSettingsService settings) : ViewModelBase
{
    private readonly AppSettingsService _settings = settings;

    public event Action? SetupCompleted;
    public event Action? SetupDismissed;

    public bool CanDismiss { get; private set; }

    // ── Step ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial WizardStep CurrentStep { get; set; } = WizardStep.Welcome;

    public bool IsWelcomeStep => CurrentStep == WizardStep.Welcome;
    public bool IsDetectStep => CurrentStep == WizardStep.Detect;
    public bool IsDirectoriesStep => CurrentStep == WizardStep.Directories;
    public bool IsInstallStep => CurrentStep == WizardStep.Install;
    public bool IsFinishStep => CurrentStep == WizardStep.Finish;

    // Step indicator dots (4 post-welcome steps)
    public bool IsStep1Active => CurrentStep >= WizardStep.Detect;
    public bool IsStep2Active => CurrentStep >= WizardStep.Directories;
    public bool IsStep3Active => CurrentStep >= WizardStep.Install;
    public bool IsStep4Active => CurrentStep >= WizardStep.Finish;

    public bool IsBackVisible =>
        CurrentStep is WizardStep.Detect or WizardStep.Directories or WizardStep.Install;

    public bool CanGoNext =>
        CurrentStep switch
        {
            WizardStep.Detect => !IsDetecting,
            WizardStep.Install => InstallSuccess || !UvFound,
            _ => true,
        };

    // ── Detection ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    public ObservableCollection<ToolStatusViewModel> Tools { get; } = [];

    [ObservableProperty]
    public partial bool AudioSeparatorFound { get; set; }

    [ObservableProperty]
    public partial string GpuHint { get; set; } = string.Empty;

    // ── GPU variant ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial GpuVariant GpuVariant { get; set; } = GpuVariant.Cpu;

    public bool IsCpu => GpuVariant == GpuVariant.Cpu;
    public bool IsCuda => GpuVariant == GpuVariant.Cuda;
    public bool IsDirectML => GpuVariant == GpuVariant.DirectML;

    // ── Directories ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = settings.Current.OutputDirectory;

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; } = settings.Current.ModelsDirectory;

    // ── Install ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool UvFound { get; set; }

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial bool InstallSuccess { get; set; }

    private readonly StringBuilder _installLog = new();

    [ObservableProperty]
    public partial string InstallLog { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? InstallError { get; set; }

    // ── Partial hooks ─────────────────────────────────────────────────────────

    partial void OnCurrentStepChanged(WizardStep value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsDetectStep));
        OnPropertyChanged(nameof(IsDirectoriesStep));
        OnPropertyChanged(nameof(IsInstallStep));
        OnPropertyChanged(nameof(IsFinishStep));
        OnPropertyChanged(nameof(IsStep1Active));
        OnPropertyChanged(nameof(IsStep2Active));
        OnPropertyChanged(nameof(IsStep3Active));
        OnPropertyChanged(nameof(IsStep4Active));
        OnPropertyChanged(nameof(IsBackVisible));
        OnPropertyChanged(nameof(CanGoNext));

        if (value == WizardStep.Detect)
            _ = RunDetectionAsync();
        else if (value == WizardStep.Install)
            _ = CheckUvAsync();
    }

    partial void OnIsDetectingChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    partial void OnGpuVariantChanged(GpuVariant value)
    {
        OnPropertyChanged(nameof(IsCpu));
        OnPropertyChanged(nameof(IsCuda));
        OnPropertyChanged(nameof(IsDirectML));
    }

    partial void OnInstallSuccessChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    partial void OnUvFoundChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Start() => CurrentStep = WizardStep.Detect;

    [RelayCommand]
    private void Back()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.Detect => WizardStep.Welcome,
            WizardStep.Directories => WizardStep.Detect,
            WizardStep.Install => WizardStep.Directories,
            _ => CurrentStep,
        };
    }

    [RelayCommand]
    private void Next()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.Detect => WizardStep.Directories,
            WizardStep.Directories when !AudioSeparatorFound => WizardStep.Install,
            WizardStep.Directories => WizardStep.Finish,
            WizardStep.Install => WizardStep.Finish,
            _ => CurrentStep,
        };
    }

    [RelayCommand]
    private void SetGpuVariant(string variant) => GpuVariant = Enum.Parse<GpuVariant>(variant);

    [RelayCommand]
    private async Task InstallAsync()
    {
        IsInstalling = true;
        InstallError = null;
        _installLog.Clear();
        InstallLog = string.Empty;

        try
        {
            var progress = new Progress<string>(line =>
            {
                if (_installLog.Length > 0)
                    _installLog.Append('\n');
                _installLog.Append(line);
                InstallLog = _installLog.ToString();
            });
            await ToolInstaller.InstallAudioSeparatorAsync(GpuVariant, progress);
            InstallSuccess = true;
            AudioSeparatorFound = true;
        }
        catch (Exception ex)
        {
            InstallError = ex.Message;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        var s = _settings.Current;
        s.GpuVariant = GpuVariant;
        s.OutputDirectory = OutputDirectory;
        s.ModelsDirectory = ModelsDirectory;
        s.FirstRunComplete = true;
        if (InstallSuccess)
            s.InstalledVariant = GpuVariant;
        await _settings.SaveAsync();
        SetupCompleted?.Invoke();
    }

    [RelayCommand]
    private void Dismiss() => SetupDismissed?.Invoke();

    public void Reset()
    {
        CanDismiss = true;
        OnPropertyChanged(nameof(CanDismiss));
        CurrentStep = WizardStep.Welcome;
        Tools.Clear();
        GpuHint = string.Empty;
        AudioSeparatorFound = false;
        UvFound = false;
        IsInstalling = false;
        InstallSuccess = false;
        InstallError = null;
        _installLog.Clear();
        InstallLog = string.Empty;
        OutputDirectory = _settings.Current.OutputDirectory;
        ModelsDirectory = _settings.Current.ModelsDirectory;
        GpuVariant = _settings.Current.GpuVariant;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunDetectionAsync()
    {
        IsDetecting = true;
        Tools.Clear();
        GpuHint = string.Empty;

        try
        {
            var toolsTask = SetupDetector.DetectAllAsync(null);
            var gpuTask = GpuDetector.DetectAsync();
            await Task.WhenAll(toolsTask, gpuTask);

            var tools = await toolsTask;
            var gpus = await gpuTask;

            await TryFillInstalledVariantAsync(tools);

            foreach (var t in tools)
                Tools.Add(new ToolStatusViewModel(t, VariantTagFor(t.Name, t.Found)));

            AudioSeparatorFound =
                tools.FirstOrDefault(t => t.Name == "audio-separator")?.Found ?? false;

            var best = gpus.OrderBy(g =>
                    g.Vendor switch
                    {
                        GpuVendor.Nvidia => 0,
                        GpuVendor.Amd => 1,
                        GpuVendor.Intel => 2,
                        _ => 3,
                    }
                )
                .FirstOrDefault();

            if (best is not null)
            {
                GpuHint = $"Detected: {best.Name}";
                GpuVariant = GpuDetector.SuggestVariant(gpus);
            }
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private async Task CheckUvAsync() => UvFound = await ToolInstaller.IsUvAvailableAsync();

    private async Task TryFillInstalledVariantAsync(IReadOnlyList<ToolInfo> tools)
    {
        if (_settings.Current.InstalledVariant is not null) return;
        if (!(tools.FirstOrDefault(t => t.Name == "audio-separator")?.Found ?? false)) return;
        var detected = await ToolInstaller.DetectInstalledVariantAsync();
        if (detected is not null)
            _settings.Current.InstalledVariant = detected;
    }

    private string? VariantTagFor(string toolName, bool found)
    {
        if (toolName != "audio-separator" || !found)
            return null;
        return _settings.Current.InstalledVariant switch
        {
            GpuVariant.Cuda => "CUDA",
            GpuVariant.DirectML => "DirectML",
            GpuVariant.Cpu => "CPU",
            _ => null,
        };
    }
}

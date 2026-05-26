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

public partial class SetupWizardViewModel(
    AppSettings settings,
    SetupDetector setupDetector,
    GpuDetector gpuDetector,
    ToolInstaller toolInstaller,
    FfmpegFetcher ffmpegFetcher,
    ToolStateService toolState,
    AppPaths paths
) : ViewModelBase
{
    private readonly AppSettings _settings = settings;
    private readonly SetupDetector _setupDetector = setupDetector;
    private readonly GpuDetector _gpuDetector = gpuDetector;
    private readonly ToolInstaller _toolInstaller = toolInstaller;
    private readonly FfmpegFetcher _ffmpegFetcher = ffmpegFetcher;
    private readonly ToolStateService _toolState = toolState;
    private readonly AppPaths _paths = paths;

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
            WizardStep.Install => AudioSeparatorFound,
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
    public partial string OutputDirectory { get; set; } = paths.OutputDirectory;

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; } = paths.ModelsDirectory;

    // ── Install ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool UvFound { get; set; }

    [ObservableProperty]
    public partial bool IsInstallingUv { get; set; }

    [ObservableProperty]
    public partial bool UvInstallSuccess { get; set; }

    [ObservableProperty]
    public partial string? UvInstallError { get; set; }

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial bool InstallSuccess { get; set; }

    [ObservableProperty]
    public partial bool YtdlpFound { get; set; }

    [ObservableProperty]
    public partial bool IsInstallingYtdlp { get; set; }

    [ObservableProperty]
    public partial bool YtdlpInstallSuccess { get; set; }

    [ObservableProperty]
    public partial string? YtdlpInstallError { get; set; }

    [ObservableProperty]
    public partial bool FfmpegFound { get; set; }

    [ObservableProperty]
    public partial bool IsInstallingFfmpeg { get; set; }

    [ObservableProperty]
    public partial bool FfmpegInstallSuccess { get; set; }

    [ObservableProperty]
    public partial string? FfmpegInstallError { get; set; }

    private readonly StringBuilder _installLog = new();

    [ObservableProperty]
    public partial string InstallLog { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? InstallError { get; set; }

    // Checkbox-style "what to install" — pre-checked for missing tools.
    [ObservableProperty]
    public partial bool WantInstallUv { get; set; }

    [ObservableProperty]
    public partial bool WantInstallAudioSeparator { get; set; }

    [ObservableProperty]
    public partial bool WantInstallYtdlp { get; set; }

    [ObservableProperty]
    public partial bool WantInstallFfmpeg { get; set; }

    public bool IsAnyInstallInProgress =>
        IsInstallingUv || IsInstalling || IsInstallingYtdlp || IsInstallingFfmpeg;

    public bool HasAnythingToInstall =>
        WantInstallUv || WantInstallAudioSeparator || WantInstallYtdlp || WantInstallFfmpeg;

    partial void OnWantInstallUvChanged(bool value) =>
        OnPropertyChanged(nameof(HasAnythingToInstall));

    partial void OnWantInstallAudioSeparatorChanged(bool value) =>
        OnPropertyChanged(nameof(HasAnythingToInstall));

    partial void OnWantInstallYtdlpChanged(bool value) =>
        OnPropertyChanged(nameof(HasAnythingToInstall));

    partial void OnWantInstallFfmpegChanged(bool value) =>
        OnPropertyChanged(nameof(HasAnythingToInstall));

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
            _ = RecheckToolsAsync();
    }

    partial void OnIsDetectingChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    partial void OnAudioSeparatorFoundChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    partial void OnGpuVariantChanged(GpuVariant value)
    {
        OnPropertyChanged(nameof(IsCpu));
        OnPropertyChanged(nameof(IsCuda));
        OnPropertyChanged(nameof(IsDirectML));
    }

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
            WizardStep.Directories => WizardStep.Install,
            WizardStep.Install => WizardStep.Finish,
            _ => CurrentStep,
        };
    }

    [RelayCommand]
    private void SetGpuVariant(string variant) => GpuVariant = Enum.Parse<GpuVariant>(variant);

    [RelayCommand]
    private async Task InstallUvAsync()
    {
        IsInstallingUv = true;
        UvInstallError = null;

        try
        {
            await _toolInstaller.InstallUvAsync(NewLogProgress());
            await _toolState.RefreshAsync("uv");
            UvFound = _toolState.IsUvAvailable;
            if (!UvFound)
                UvInstallError =
                    "uv installed but could not be found on PATH. You may need to restart StemForge.";
            else
                UvInstallSuccess = true;
        }
        catch (Exception ex)
        {
            UvInstallError = ex.Message;
        }
        finally
        {
            IsInstallingUv = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        IsInstalling = true;
        InstallError = null;

        try
        {
            await _toolInstaller.InstallAudioSeparatorAsync(GpuVariant, NewLogProgress());
            await _toolState.RefreshAsync("audio-separator");
            AudioSeparatorFound = _toolState.IsAudioSeparatorAvailable;
            InstallSuccess = AudioSeparatorFound;
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
    private async Task InstallYtdlpAsync()
    {
        IsInstallingYtdlp = true;
        YtdlpInstallError = null;

        try
        {
            await _toolInstaller.InstallYtdlpAsync(NewLogProgress());
            await _toolState.RefreshAsync("yt-dlp");
            YtdlpFound = _toolState.IsYtdlpAvailable;
            if (!YtdlpFound)
                YtdlpInstallError =
                    "yt-dlp installed but could not be found on PATH. You may need to restart StemForge.";
            else
                YtdlpInstallSuccess = true;
        }
        catch (Exception ex)
        {
            YtdlpInstallError = ex.Message;
        }
        finally
        {
            IsInstallingYtdlp = false;
        }
    }

    [RelayCommand]
    private async Task InstallFfmpegAsync()
    {
        IsInstallingFfmpeg = true;
        FfmpegInstallError = null;

        try
        {
            IProgress<string> lineProgress = NewLogProgress();
            var fetchProgress = new Progress<FfmpegFetchProgress>(p =>
            {
                if (p.TotalBytes is { } total && total > 0)
                {
                    var pct = p.BytesDownloaded * 100.0 / total;
                    lineProgress.Report(
                        $"{p.Phase}: {FormatMB(p.BytesDownloaded)} / {FormatMB(total)} ({pct:F0}%)"
                    );
                }
                else
                {
                    lineProgress.Report(p.Phase);
                }
            });

            await _ffmpegFetcher.FetchAsync(fetchProgress);
            await _toolState.RefreshAsync("ffmpeg");
            FfmpegFound = _toolState.IsFfmpegAvailable;
            if (!FfmpegFound)
                FfmpegInstallError =
                    "ffmpeg downloaded but could not be invoked. Check the install log above.";
            else
                FfmpegInstallSuccess = true;
        }
        catch (Exception ex)
        {
            FfmpegInstallError = ex.Message;
        }
        finally
        {
            IsInstallingFfmpeg = false;
        }
    }

    private static string FormatMB(long bytes) => $"{bytes / 1_048_576.0:F1} MB";

    private Progress<string> NewLogProgress()
    {
        _installLog.Clear();
        InstallLog = string.Empty;
        return new Progress<string>(line =>
        {
            if (_installLog.Length > 0)
                _installLog.Append('\n');
            _installLog.Append(line);
            InstallLog = _installLog.ToString();
        });
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        _settings.GpuVariant = GpuVariant;
        _settings.OutputDirectory = NullIfBlank(OutputDirectory);
        _settings.ModelsDirectory = NullIfBlank(ModelsDirectory);
        _settings.FirstRunComplete = true;
        if (InstallSuccess)
            _settings.InstalledVariant = GpuVariant;
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
        IsInstallingUv = false;
        UvInstallError = null;
        IsInstalling = false;
        InstallSuccess = false;
        InstallError = null;
        YtdlpFound = false;
        IsInstallingYtdlp = false;
        YtdlpInstallSuccess = false;
        YtdlpInstallError = null;
        FfmpegFound = false;
        IsInstallingFfmpeg = false;
        FfmpegInstallSuccess = false;
        FfmpegInstallError = null;
        _installLog.Clear();
        InstallLog = string.Empty;
        OutputDirectory = _paths.OutputDirectory;
        ModelsDirectory = _paths.ModelsDirectory;
        GpuVariant = _settings.GpuVariant;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunDetectionAsync()
    {
        IsDetecting = true;
        Tools.Clear();
        GpuHint = string.Empty;

        try
        {
            var toolsTask = _setupDetector.DetectAllAsync();
            var gpuTask = _gpuDetector.DetectAsync();
            await Task.WhenAll(toolsTask, gpuTask);

            var tools = await toolsTask;
            var gpus = await gpuTask;

            await TryFillInstalledVariantAsync(tools);

            foreach (var t in tools)
                Tools.Add(new ToolStatusViewModel(t, VariantTagFor(t.Name, t.Found)));

            UvFound = tools.FirstOrDefault(t => t.Name == "uv")?.Found ?? false;
            AudioSeparatorFound =
                tools.FirstOrDefault(t => t.Name == "audio-separator")?.Found ?? false;
            YtdlpFound = tools.FirstOrDefault(t => t.Name == "yt-dlp")?.Found ?? false;
            FfmpegFound = tools.FirstOrDefault(t => t.Name == "ffmpeg")?.Found ?? false;

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
                GpuVariant = GpuDetector.SuggestVariant(gpus); // static pure method
            }
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private async Task RecheckToolsAsync()
    {
        await _toolState.RefreshAsync();
        UvFound = _toolState.IsUvAvailable;
        YtdlpFound = _toolState.IsYtdlpAvailable;
        FfmpegFound = _toolState.IsFfmpegAvailable;
        AudioSeparatorFound = _toolState.IsAudioSeparatorAvailable;

        // Pre-check anything missing so the user just clicks Install.
        WantInstallUv = !UvFound;
        WantInstallAudioSeparator = !AudioSeparatorFound;
        WantInstallYtdlp = !YtdlpFound;
        WantInstallFfmpeg = !FfmpegFound;
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        // Order matters: uv must be available before audio-separator/yt-dlp.
        if (WantInstallUv && !UvFound)
            await InstallUvAsync();
        if (WantInstallAudioSeparator && UvFound && !AudioSeparatorFound)
            await InstallAsync();
        if (WantInstallYtdlp && UvFound && !YtdlpFound)
            await InstallYtdlpAsync();
        if (WantInstallFfmpeg && !FfmpegFound)
            await InstallFfmpegAsync();
    }

    private async Task TryFillInstalledVariantAsync(IReadOnlyList<ToolInfo> tools)
    {
        if (_settings.InstalledVariant is not null)
            return;
        if (!(tools.FirstOrDefault(t => t.Name == "audio-separator")?.Found ?? false))
            return;
        var detected = await _toolInstaller.DetectInstalledVariantAsync();
        if (detected is not null)
            _settings.InstalledVariant = detected;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private string? VariantTagFor(string toolName, bool found)
    {
        if (toolName != "audio-separator" || !found)
            return null;
        return _settings.InstalledVariant switch
        {
            GpuVariant.Cuda => "CUDA",
            GpuVariant.DirectML => "DirectML",
            GpuVariant.Cpu => "CPU",
            _ => null,
        };
    }
}

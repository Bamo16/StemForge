using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
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
    ToolStateService toolState,
    AppPaths paths,
    PlatformInfo platform
) : ViewModelBase, IVariantPicker
{
    ICommand IVariantPicker.SetVariantCommand => SetGpuVariantCommand;

    private readonly AppSettings _settings = settings;
    private readonly SetupDetector _setupDetector = setupDetector;
    private readonly GpuDetector _gpuDetector = gpuDetector;
    private readonly ToolInstaller _toolInstaller = toolInstaller;
    private readonly ToolStateService _toolState = toolState;
    private readonly AppPaths _paths = paths;

    // GPU variants audio-separator offers on the running OS; drives which picker buttons show.
    private readonly IReadOnlyList<GpuVariant> _availableVariants = ToolCatalog
        .Get(ToolKind.AudioSeparator)
        .InstallStrategy
        is UvToolInstall uv
        ? [.. uv.VariantsFor(platform.Os).Select(v => v.Variant)]
        : [];

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
            // All required tools must be present before leaving the Install step. Optional tools
            // (yt-dlp, deno) never block.
            WizardStep.Install => InstallRows.Count > 0
                && InstallRows.Where(r => r.IsRequired).All(r => r.Found),
            _ => true,
        };

    // ── Detection ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    public ObservableCollection<ToolStatusViewModel> Tools { get; } = [];

    [ObservableProperty]
    public partial string GpuHint { get; set; } = string.Empty;

    // ── GPU variant ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial GpuVariant GpuVariant { get; set; } = GpuVariant.Cpu;

    public bool IsCpu => GpuVariant == GpuVariant.Cpu;
    public bool IsCuda => GpuVariant == GpuVariant.Cuda;
    public bool IsDirectML => GpuVariant == GpuVariant.DirectML;

    public bool HasCpuVariant => _availableVariants.Contains(GpuVariant.Cpu);
    public bool HasCudaVariant => _availableVariants.Contains(GpuVariant.Cuda);
    public bool HasDirectMLVariant => _availableVariants.Contains(GpuVariant.DirectML);

    // ── Directories ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = paths.OutputDirectory;

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; } = paths.ModelsDirectory;

    // ── Install ───────────────────────────────────────────────────────────────

    public ObservableCollection<ToolRowViewModel> InstallRows { get; } = [];

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    private readonly StringBuilder _installLog = new();

    [ObservableProperty]
    public partial string InstallLog { get; set; } = string.Empty;

    public bool HasAnythingToInstall => InstallRows.Any(r => r.WantInstall && !r.Found);

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
        {
            EnsureInstallRows();
            _ = RecheckToolsAsync();
        }
    }

    partial void OnIsDetectingChanged(bool value) => OnPropertyChanged(nameof(CanGoNext));

    partial void OnGpuVariantChanged(GpuVariant value)
    {
        OnPropertyChanged(nameof(IsCpu));
        OnPropertyChanged(nameof(IsCuda));
        OnPropertyChanged(nameof(IsDirectML));
    }

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
    private async Task InstallSelectedAsync()
    {
        IsInstalling = true;
        _installLog.Clear();
        InstallLog = string.Empty;
        try
        {
            // uv must be present before audio-separator (uv installs it). Required tools first.
            await InstallRowAsync(ToolKind.Uv);
            if (_toolState.IsAvailable(ToolKind.Uv))
                await InstallRowAsync(ToolKind.AudioSeparator);
            await InstallRowAsync(ToolKind.Ffmpeg);
            await InstallRowAsync(ToolKind.Ytdlp);
            await InstallRowAsync(ToolKind.Deno);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        _settings.GpuVariant = GpuVariant;
        _settings.OutputDirectory = NullIfBlank(OutputDirectory);
        _settings.ModelsDirectory = NullIfBlank(ModelsDirectory);
        _settings.FirstRunComplete = true;
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
        foreach (var row in InstallRows)
        {
            row.PropertyChanged -= OnInstallRowChanged;
            row.IsInstalling = false;
            row.InProgressMessage = string.Empty;
        }
        InstallRows.Clear();
        GpuHint = string.Empty;
        IsInstalling = false;
        _installLog.Clear();
        InstallLog = string.Empty;
        OutputDirectory = _paths.OutputDirectory;
        ModelsDirectory = _paths.ModelsDirectory;
        GpuVariant = _settings.GpuVariant;
        OnPropertyChanged(nameof(HasAnythingToInstall));
        OnPropertyChanged(nameof(CanGoNext));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunDetectionAsync()
    {
        IsDetecting = true;
        Tools.Clear();
        GpuHint = string.Empty;

        try
        {
            // Drive detection through ToolStateService so its Tools list is populated by the
            // time the user advances to the Install step; otherwise EnsureInstallRows would
            // briefly render every tool as "not installed" until the async recheck completes.
            var refreshTask = _toolState.RefreshAsync();
            var gpuTask = _gpuDetector.DetectAsync();
            await Task.WhenAll(refreshTask, gpuTask);

            var tools = _toolState.Tools;
            var gpus = await gpuTask;

            await TryFillInstalledVariantAsync(tools);

            foreach (var t in tools)
                Tools.Add(new ToolStatusViewModel(t, VariantTagFor(t.Kind, t.Found)));

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

    private void EnsureInstallRows()
    {
        if (InstallRows.Count > 0)
            return;

        foreach (var tool in ToolCatalog.All)
        {
            // Audio-separator's row gets the wizard as its variant picker so the inline
            // CPU/CUDA/DirectML buttons can bind directly to a non-null path.
            var picker = tool.Kind == ToolKind.AudioSeparator ? this : null;
            var isAvailable = _toolState.IsAvailable(tool.Kind);
            var row = new ToolRowViewModel(tool, picker)
            {
                Found = isAvailable,
                WantInstall = !isAvailable,
            };
            row.PropertyChanged += OnInstallRowChanged;
            InstallRows.Add(row);
        }

        // The initial Found/WantInstall sets above happen before the row's PropertyChanged
        // subscription is wired, and the subsequent RecheckToolsAsync re-set with the same
        // values is suppressed by the observable-property change check — so neither path
        // raises the wizard-level aggregates. Raise them here so the Install Selected button
        // and Next gate reflect the freshly-built rows.
        OnPropertyChanged(nameof(HasAnythingToInstall));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void OnInstallRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName is nameof(ToolRowViewModel.WantInstall) or nameof(ToolRowViewModel.Found)
        )
        {
            OnPropertyChanged(nameof(HasAnythingToInstall));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    private async Task RecheckToolsAsync()
    {
        await _toolState.RefreshAsync();
        foreach (var row in InstallRows)
        {
            row.Found = _toolState.IsAvailable(row.Kind);
            row.WantInstall = !row.Found; // pre-check anything missing so the user just clicks Install
        }
    }

    private async Task InstallRowAsync(ToolKind kind)
    {
        var row = InstallRows.FirstOrDefault(r => r.Kind == kind);
        if (row is null || !row.WantInstall || row.Found)
            return;

        row.InstallError = null;
        row.IsInstalling = true;
        // audio-separator pulls torch/onnxruntime (250 MB to 2 GB), so warn it is slow. Other tools
        // are quick; an empty message just shows the indeterminate bar with no extra line.
        row.InProgressMessage =
            kind == ToolKind.AudioSeparator
                ? "Downloading and installing audio-separator. This can take several minutes."
                : string.Empty;
        var options = new InstallOptions(kind == ToolKind.AudioSeparator ? GpuVariant : null);

        try
        {
            await _toolInstaller.InstallAsync(ToolCatalog.Get(kind), options, NewLogProgress());
            await _toolState.RefreshAsync(kind);
            row.Found = _toolState.IsAvailable(kind);

            if (!row.Found)
                row.InstallError =
                    $"{row.Name} installed but could not be found. You may need to restart StemForge.";
            else
            {
                row.InstallSucceeded = true;
                if (kind == ToolKind.AudioSeparator)
                    _settings.InstalledVariant = GpuVariant;
            }
        }
        catch (Exception ex)
        {
            row.InstallError = ex.Message;
        }
        finally
        {
            row.IsInstalling = false;
            row.InProgressMessage = string.Empty;
        }
    }

    private Progress<InstallProgress> NewLogProgress() =>
        new(p =>
        {
            var line =
                p is { BytesDownloaded: { } done, TotalBytes: { } total } && total > 0
                    ? $"{p.Message}: {FormatMB(done)} / {FormatMB(total)} ({done * 100.0 / total:F0}%)"
                    : p.Message;

            if (_installLog.Length > 0)
                _installLog.Append('\n');
            _installLog.Append(line);
            InstallLog = _installLog.ToString();
        });

    private static string FormatMB(long bytes) => $"{bytes / 1_048_576.0:F1} MB";

    private async Task TryFillInstalledVariantAsync(IReadOnlyList<ToolState> tools)
    {
        if (_settings.InstalledVariant is not null)
            return;
        if (!(tools.FirstOrDefault(t => t.Kind == ToolKind.AudioSeparator)?.Found ?? false))
            return;
        var detected = await _toolInstaller.DetectInstalledVariantAsync();
        if (detected is not null)
            _settings.InstalledVariant = detected;
        // If detection fails, InstalledVariant stays null. This only affects the variant tag
        // shown in Settings — it has no effect on audio-separator's runtime behavior.
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private string? VariantTagFor(ToolKind kind, bool found)
    {
        if (kind != ToolKind.AudioSeparator || !found)
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

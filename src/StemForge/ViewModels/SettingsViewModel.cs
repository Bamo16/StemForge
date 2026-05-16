using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public partial class SettingsViewModel : PageViewModelBase
{
    private readonly AppSettings _settings;
    private readonly GpuDetector _gpuDetector;
    private readonly ToolInstaller _toolInstaller;
    private readonly ToolStateService _toolState;

    public override string Title => "Settings";

    // ── Tool detection ─────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool ToolsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool AllSystemsGo { get; set; }

    [ObservableProperty]
    public partial string GpuHint { get; set; } = string.Empty;
    public ObservableCollection<ToolStatusViewModel> Tools { get; } = [];

    // ── GPU variant ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial GpuVariant GpuVariant { get; set; }

    public bool IsCpu => GpuVariant == GpuVariant.Cpu;
    public bool IsCuda => GpuVariant == GpuVariant.Cuda;
    public bool IsDirectML => GpuVariant == GpuVariant.DirectML;

    // ── audio-separator lifecycle ──────────────────────────────────────────────

    public bool IsAudioSeparatorInstalled => _toolState.IsAudioSeparatorAvailable;

    public string InstalledVariantLabel =>
        _settings.InstalledVariant switch
        {
            Models.GpuVariant.Cuda => "CUDA",
            Models.GpuVariant.DirectML => "DirectML",
            Models.GpuVariant.Cpu => "CPU",
            _ => "Unknown",
        };

    [ObservableProperty]
    public partial bool IsChangeVariantOpen { get; set; }

    [ObservableProperty]
    public partial bool IsReinstallConfirmOpen { get; set; }

    [ObservableProperty]
    public partial bool IsUninstallConfirmOpen { get; set; }

    [ObservableProperty]
    public partial GpuVariant PendingVariant { get; set; }

    public bool IsPendingCpu => PendingVariant == Models.GpuVariant.Cpu;
    public bool IsPendingCuda => PendingVariant == Models.GpuVariant.Cuda;
    public bool IsPendingDirectML => PendingVariant == Models.GpuVariant.DirectML;

    [ObservableProperty]
    public partial bool IsManagingTool { get; set; }

    [ObservableProperty]
    public partial string ManageStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ManageLog { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ManageError { get; set; }

    partial void OnPendingVariantChanged(GpuVariant value)
    {
        OnPropertyChanged(nameof(IsPendingCpu));
        OnPropertyChanged(nameof(IsPendingCuda));
        OnPropertyChanged(nameof(IsPendingDirectML));
        ConfirmChangeVariantCommand.NotifyCanExecuteChanged();
    }

    // ── Directories ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; } = string.Empty;

    // ── Optional tool paths (blank = use AppPaths default) ─────────────────────

    [ObservableProperty]
    public partial string UvPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AudioSeparatorPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YtdlpPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FfmpegPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YtdlpCookiesFromBrowser { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YtdlpJsRuntime { get; set; } = string.Empty;

    // ── Default audio format ───────────────────────────────────────────────────

    [ObservableProperty]
    public partial AudioFormat DefaultAudioFormat { get; set; } = AudioFormat.Flac;

    public IReadOnlyList<AudioFormat> AudioFormatOptions { get; } =
        [AudioFormat.Flac, AudioFormat.Wav, AudioFormat.Mp3];

    // ── Save state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool SaveSuccess { get; set; }

    public SettingsViewModel(
        AppSettings settings,
        SetupDetector setupDetector,
        GpuDetector gpuDetector,
        ToolInstaller toolInstaller,
        ToolStateService toolState
    )
    {
        _settings = settings;
        _gpuDetector = gpuDetector;
        _toolInstaller = toolInstaller;
        _toolState = toolState;
        LoadFromSettings(settings);
        _toolState.PropertyChanged += OnToolStatePropertyChanged;
        SyncToolsFromState();
        _ = DetectGpuAsync();
    }

    private void OnToolStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolStateService.Tools))
        {
            SyncToolsFromState();
            OnPropertyChanged(nameof(IsAudioSeparatorInstalled));
            OnPropertyChanged(nameof(InstalledVariantLabel));
        }
        else if (e.PropertyName == nameof(ToolStateService.IsAudioSeparatorAvailable))
            OnPropertyChanged(nameof(IsAudioSeparatorInstalled));
        else if (e.PropertyName == nameof(ToolStateService.IsLoading))
            ToolsLoading = _toolState.IsLoading;
    }

    private async void SyncToolsFromState()
    {
        Tools.Clear();
        await TryFillInstalledVariantAsync(_toolState.Tools);
        foreach (var t in _toolState.Tools)
            Tools.Add(new ToolStatusViewModel(t, VariantTagFor(t.Name, t.Found)));
        AllSystemsGo = _toolState.Tools.All(t => t.Found || !t.IsRequired);
        ToolsLoading = _toolState.IsLoading;
    }

    private void LoadFromSettings(AppSettings s)
    {
        GpuVariant = s.GpuVariant;
        OutputDirectory = s.OutputDirectory ?? string.Empty;
        ModelsDirectory = s.ModelsDirectory ?? string.Empty;
        UvPath = s.UvPath ?? string.Empty;
        AudioSeparatorPath = s.AudioSeparatorPath ?? string.Empty;
        YtdlpPath = s.YtdlpPath ?? string.Empty;
        FfmpegPath = s.FfmpegPath ?? string.Empty;
        YtdlpCookiesFromBrowser = s.YtdlpCookiesFromBrowser ?? string.Empty;
        YtdlpJsRuntime = s.YtdlpJsRuntime ?? string.Empty;
        DefaultAudioFormat = s.DefaultAudioFormat;
    }

    partial void OnGpuVariantChanged(GpuVariant value)
    {
        OnPropertyChanged(nameof(IsCpu));
        OnPropertyChanged(nameof(IsCuda));
        OnPropertyChanged(nameof(IsDirectML));
    }

    [RelayCommand]
    private void SetGpuVariant(string variant)
    {
        GpuVariant = Enum.Parse<GpuVariant>(variant);
    }

    // ── audio-separator lifecycle commands ─────────────────────────────────────

    [RelayCommand]
    private void BeginChangeVariant()
    {
        CloseAllPanels();
        PendingVariant = _settings.InstalledVariant ?? GpuVariant;
        IsChangeVariantOpen = true;
    }

    [RelayCommand]
    private void SetPendingVariant(string variant) =>
        PendingVariant = Enum.Parse<GpuVariant>(variant);

    private bool CanConfirmChangeVariant =>
        !IsManagingTool && PendingVariant != _settings.InstalledVariant;

    [RelayCommand(CanExecute = nameof(CanConfirmChangeVariant))]
    private async Task ConfirmChangeVariant()
    {
        await RunInstallAsync(
            $"Switching audio-separator variant to {PendingVariant}…",
            PendingVariant
        );
    }

    [RelayCommand]
    private void BeginReinstall()
    {
        CloseAllPanels();
        IsReinstallConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmReinstall()
    {
        var variant = _settings.InstalledVariant ?? GpuVariant;
        await RunInstallAsync($"Reinstalling audio-separator ({variant})…", variant);
    }

    [RelayCommand]
    private void BeginUninstall()
    {
        CloseAllPanels();
        IsUninstallConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmUninstall()
    {
        IsUninstallConfirmOpen = false;
        await RunManageActionAsync(
            "Uninstalling audio-separator…",
            (progress, ct) => _toolInstaller.UninstallAudioSeparatorAsync(progress, ct),
            onSuccess: () =>
            {
                _settings.InstalledVariant = null;
                OnPropertyChanged(nameof(InstalledVariantLabel));
            }
        );
    }

    [RelayCommand]
    private void CancelManage() => CloseAllPanels();

    private void CloseAllPanels()
    {
        IsChangeVariantOpen = false;
        IsReinstallConfirmOpen = false;
        IsUninstallConfirmOpen = false;
        ManageError = null;
    }

    private async Task RunInstallAsync(string status, GpuVariant variant)
    {
        await RunManageActionAsync(
            status,
            (progress, ct) => _toolInstaller.InstallAudioSeparatorAsync(variant, progress, ct),
            onSuccess: () =>
            {
                _settings.InstalledVariant = variant;
                OnPropertyChanged(nameof(InstalledVariantLabel));
            }
        );
    }

    private async Task RunManageActionAsync(
        string status,
        Func<IProgress<string>, CancellationToken, Task> action,
        Action onSuccess
    )
    {
        IsManagingTool = true;
        ManageStatus = status;
        ManageLog = string.Empty;
        ManageError = null;
        var sb = new System.Text.StringBuilder();
        var progress = new Progress<string>(line =>
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(line);
            ManageLog = sb.ToString();
        });

        try
        {
            await action(progress, CancellationToken.None);
            onSuccess();
            await _toolState.RefreshAsync();
            await _settings.SaveAsync();
            CloseAllPanels();
        }
        catch (Exception ex)
        {
            ManageError = ex.Message;
        }
        finally
        {
            IsManagingTool = false;
            ManageStatus = string.Empty;
            ConfirmChangeVariantCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task DetectGpuAsync()
    {
        GpuHint = string.Empty;
        var gpus = await _gpuDetector.DetectAsync();

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
            if (!_settings.FirstRunComplete)
                GpuVariant = GpuDetector.SuggestVariant(gpus);
        }
    }

    private async Task TryFillInstalledVariantAsync(IReadOnlyList<ToolInfo> tools)
    {
        if (_settings.InstalledVariant is not null)
            return;

        if (!(tools.FirstOrDefault(t => t.Name == "audio-separator")?.Found ?? false))
            return;

        if (await _toolInstaller.DetectInstalledVariantAsync() is { } detected)
            _settings.InstalledVariant = detected;
    }

    private string? VariantTagFor(string toolName, bool found) =>
        toolName != "audio-separator" || !found
            ? null
            : _settings.InstalledVariant switch
            {
                GpuVariant.Cuda => "CUDA",
                GpuVariant.DirectML => "DirectML",
                GpuVariant.Cpu => "CPU",
                _ => null,
            };

    public event Action? ShowWizardRequested;

    [RelayCommand]
    private void ShowWizard() => ShowWizardRequested?.Invoke();

    [RelayCommand]
    private async Task RefreshTools()
    {
        await _toolState.RefreshAsync();
        await DetectGpuAsync();
    }

    [RelayCommand]
    private async Task Save()
    {
        _settings.GpuVariant = GpuVariant;
        _settings.OutputDirectory = NullIfBlank(OutputDirectory);
        _settings.ModelsDirectory = NullIfBlank(ModelsDirectory);
        _settings.UvPath = NullIfBlank(UvPath);
        _settings.AudioSeparatorPath = NullIfBlank(AudioSeparatorPath);
        _settings.YtdlpPath = NullIfBlank(YtdlpPath);
        _settings.FfmpegPath = NullIfBlank(FfmpegPath);
        _settings.YtdlpCookiesFromBrowser = NullIfBlank(YtdlpCookiesFromBrowser);
        _settings.YtdlpJsRuntime = NullIfBlank(YtdlpJsRuntime);
        _settings.DefaultAudioFormat = DefaultAudioFormat;
        _settings.FirstRunComplete = true;

        await _settings.SaveAsync();

        SaveSuccess = true;
        await Task.Delay(2000);
        SaveSuccess = false;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

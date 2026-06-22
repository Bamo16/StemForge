using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StemForge.ViewModels;

public partial class SettingsViewModel : PageViewModelBase
{
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly GpuDetector _gpuDetector;
    private readonly ToolInstaller _toolInstaller;
    private readonly ToolStateService _toolState;

    public override string Title => "Settings";

    /// <summary>Product name shown in the footer build stamp. Sourced from <see cref="IAppInfo"/>.</summary>
    public string ProductName { get; }

    /// <summary>Footer version, e.g. "v0.2.0" (rendered monospaced). Sourced from <see cref="IAppInfo"/>.</summary>
    public string VersionText { get; }

    // ── Tool rows (status header + tool paths share this collection) ──────────

    [ObservableProperty]
    public partial bool ToolsLoading { get; set; } = true;

    /// <summary>
    /// True when the "Tool paths" advanced section is expanded. Auto-set to true on load
    /// if any override is already active so users are never surprised by a hidden override.
    /// </summary>
    [ObservableProperty]
    public partial bool IsToolPathsExpanded { get; set; }

    [RelayCommand]
    private void ToggleToolPaths() => IsToolPathsExpanded = !IsToolPathsExpanded;

    [ObservableProperty]
    public partial bool AllSystemsGo { get; set; }

    [ObservableProperty]
    public partial string GpuHint { get; set; } = string.Empty;

    public ObservableCollection<SettingsToolRowViewModel> SettingsToolRows { get; } = [];

    // ── GPU variant ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial GpuVariant GpuVariant { get; set; }

    public bool IsCpu => GpuVariant == GpuVariant.Cpu;
    public bool IsCuda => GpuVariant == GpuVariant.Cuda;
    public bool IsDirectML => GpuVariant == GpuVariant.DirectML;

    // ── audio-separator lifecycle ──────────────────────────────────────────────

    public bool IsAudioSeparatorInstalled => _toolState.IsAvailable(ToolKind.AudioSeparator);

    public string InstalledVariantLabel =>
        _settings.InstalledVariant switch
        {
            GpuVariant.Cuda => "CUDA",
            GpuVariant.DirectML => "DirectML",
            GpuVariant.Cpu => "CPU",
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

    public bool IsPendingCpu => PendingVariant == GpuVariant.Cpu;
    public bool IsPendingCuda => PendingVariant == GpuVariant.Cuda;
    public bool IsPendingDirectML => PendingVariant == GpuVariant.DirectML;

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

    [ObservableProperty]
    public partial string YtdlpCookiesFromBrowser { get; set; } = string.Empty;

    // ── Default audio format ───────────────────────────────────────────────────

    [ObservableProperty]
    public partial AudioFormat DefaultAudioFormat { get; set; } = AudioFormat.Flac;

    public IReadOnlyList<AudioFormat> AudioFormatOptions { get; } =
    [AudioFormat.Flac, AudioFormat.Wav, AudioFormat.Mp3];

    // ── Drum extraction ────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string DrumExtractionModel { get; set; } = string.Empty;

    /// <summary>True = DrumStemLocation.WithStems; false = CacheOnly.</summary>
    [ObservableProperty]
    public partial bool DrumStemsWithOutputs { get; set; } = true;

    // ── Save state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool SaveSuccess { get; set; }

    public SettingsViewModel(
        AppSettings settings,
        AppPaths paths,
        SetupDetector setupDetector,
        GpuDetector gpuDetector,
        ToolInstaller toolInstaller,
        ToolStateService toolState,
        IAppInfo appInfo
    )
    {
        _settings = settings;
        _paths = paths;
        _gpuDetector = gpuDetector;
        _toolInstaller = toolInstaller;
        _toolState = toolState;
        ProductName = appInfo.ProductName;
        VersionText = $"v{appInfo.ShortVersion}";

        foreach (var tool in ToolCatalog.All)
            SettingsToolRows.Add(new SettingsToolRowViewModel(tool));

        LoadFromSettings(settings);
        RefreshResolvedPaths();
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
        // Snapshot first, then await, then mutate the rows in one synchronous block, so
        // concurrent ToolStateService updates don't double-apply variant probes.
        var snapshot = _toolState.Tools;
        await TryFillInstalledVariantAsync(snapshot);

        foreach (var row in SettingsToolRows)
        {
            var info = snapshot.FirstOrDefault(t => t.Kind == row.Kind);
            row.Found = info?.Found ?? false;
            row.Version = info?.Version ?? string.Empty;
            row.VariantTag = VariantTagFor(row.Kind, row.Found);
        }

        AllSystemsGo = snapshot.All(t => t.Found || !t.IsRequired);
        ToolsLoading = _toolState.IsLoading;
    }

    private void LoadFromSettings(AppSettings s)
    {
        GpuVariant = s.GpuVariant;
        OutputDirectory = s.OutputDirectory ?? string.Empty;
        ModelsDirectory = s.ModelsDirectory ?? string.Empty;
        foreach (var row in SettingsToolRows)
            row.PathOverride = s.GetToolPathOverride(row.Kind) ?? string.Empty;
        YtdlpCookiesFromBrowser = s.YtdlpCookiesFromBrowser ?? string.Empty;
        DefaultAudioFormat = s.DefaultAudioFormat;
        DrumExtractionModel = s.DrumExtractionModel;
        DrumStemsWithOutputs = s.DrumStemLocation == DrumStemLocation.WithStems;

        // Auto-expand if any override is already active so users are never surprised.
        if (SettingsToolRows.Any(r => !string.IsNullOrEmpty(r.PathOverride)))
            IsToolPathsExpanded = true;
    }

    private void RefreshResolvedPaths()
    {
        foreach (var row in SettingsToolRows)
            row.ResolvedPath = _paths.PathFor(row.Kind);
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
            (progress, ct) =>
                _toolInstaller.UninstallAsync(
                    ToolCatalog.Get(ToolKind.AudioSeparator),
                    progress,
                    ct
                ),
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
            (progress, ct) =>
                _toolInstaller.InstallAsync(
                    ToolCatalog.Get(ToolKind.AudioSeparator),
                    new InstallOptions(variant),
                    progress,
                    ct
                ),
            onSuccess: () =>
            {
                _settings.InstalledVariant = variant;
                OnPropertyChanged(nameof(InstalledVariantLabel));
            }
        );
    }

    private async Task RunManageActionAsync(
        string status,
        Func<IProgress<InstallProgress>, CancellationToken, Task> action,
        Action onSuccess
    )
    {
        IsManagingTool = true;
        ManageStatus = status;
        ManageLog = string.Empty;
        ManageError = null;
        var sb = new System.Text.StringBuilder();
        var progress = new Progress<InstallProgress>(p =>
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(p.Message);
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

    private async Task TryFillInstalledVariantAsync(IReadOnlyList<ToolState> tools)
    {
        if (_settings.InstalledVariant is not null)
            return;

        if (!(tools.FirstOrDefault(t => t.Kind == ToolKind.AudioSeparator)?.Found ?? false))
            return;

        if (await _toolInstaller.DetectInstalledVariantAsync() is { } detected)
            _settings.InstalledVariant = detected;
    }

    private string? VariantTagFor(ToolKind kind, bool found) =>
        kind != ToolKind.AudioSeparator || !found
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
        foreach (var row in SettingsToolRows)
            _settings.SetToolPathOverride(row.Kind, NullIfBlank(row.PathOverride));
        _settings.YtdlpCookiesFromBrowser = NullIfBlank(YtdlpCookiesFromBrowser);
        _settings.DefaultAudioFormat = DefaultAudioFormat;
        _settings.DrumExtractionModel = string.IsNullOrWhiteSpace(DrumExtractionModel)
            ? "htdemucs_ft.yaml"
            : DrumExtractionModel;
        _settings.DrumStemLocation = DrumStemsWithOutputs
            ? DrumStemLocation.WithStems
            : DrumStemLocation.CacheOnly;
        _settings.FirstRunComplete = true;

        await _settings.SaveAsync();
        RefreshResolvedPaths();

        SaveSuccess = true;
        await Task.Delay(2000);
        SaveSuccess = false;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

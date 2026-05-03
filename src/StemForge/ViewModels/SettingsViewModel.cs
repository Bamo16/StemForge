using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public partial class SettingsViewModel : PageViewModelBase
{
    private readonly AppSettingsService _settingsService;

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

    // ── Directories ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; } = string.Empty;

    // ── Optional tool paths ────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string YtdlpPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YtdlpCookiesFromBrowser { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YtdlpJsRuntime { get; set; } = string.Empty;

    // ── Save state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool SaveSuccess { get; set; }

    public SettingsViewModel(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromSettings(settingsService.Current);
        _ = DetectToolsAsync();
    }

    private void LoadFromSettings(AppSettings s)
    {
        GpuVariant = s.GpuVariant;
        OutputDirectory = s.OutputDirectory;
        ModelsDirectory = s.ModelsDirectory;
        YtdlpPath = s.YtdlpPath ?? string.Empty;
        YtdlpCookiesFromBrowser = s.YtdlpCookiesFromBrowser ?? string.Empty;
        YtdlpJsRuntime = s.YtdlpJsRuntime ?? string.Empty;
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

    public async Task DetectToolsAsync()
    {
        ToolsLoading = true;
        Tools.Clear();
        GpuHint = string.Empty;
        try
        {
            var toolsTask = SetupDetector.DetectAllAsync(
                string.IsNullOrWhiteSpace(YtdlpPath) ? null : YtdlpPath
            );
            var gpuTask = GpuDetector.DetectAsync();

            await Task.WhenAll(toolsTask, gpuTask);

            var results = await toolsTask;
            var gpus = await gpuTask;

            await TryFillInstalledVariantAsync(results);

            foreach (var t in results)
                Tools.Add(new ToolStatusViewModel(t, VariantTagFor(t.Name, t.Found)));
            AllSystemsGo = results.All(t => t.Found || !t.IsRequired);

            // Pick the most capable GPU for the hint (NVIDIA > AMD/Intel > unknown)
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

                // Auto-select the best variant only before the user has ever saved settings
                if (!_settingsService.Current.FirstRunComplete)
                    GpuVariant = GpuDetector.SuggestVariant(gpus);
            }
        }
        finally
        {
            ToolsLoading = false;
        }
    }

    private async Task TryFillInstalledVariantAsync(IReadOnlyList<ToolInfo> tools)
    {
        if (_settingsService.Current.InstalledVariant is not null)
            return;

        if (!(tools.FirstOrDefault(t => t.Name == "audio-separator")?.Found ?? false))
            return;

        if (await ToolInstaller.DetectInstalledVariantAsync() is { } detected)
            _settingsService.Current.InstalledVariant = detected;
    }

    private string? VariantTagFor(string toolName, bool found)
    {
        if (toolName != "audio-separator" || !found)
            return null;
        return _settingsService.Current.InstalledVariant switch
        {
            GpuVariant.Cuda => "CUDA",
            GpuVariant.DirectML => "DirectML",
            GpuVariant.Cpu => "CPU",
            _ => null,
        };
    }

    public event Action? ShowWizardRequested;

    [RelayCommand]
    private void ShowWizard() => ShowWizardRequested?.Invoke();

    [RelayCommand]
    private async Task RefreshTools() => await DetectToolsAsync();

    [RelayCommand]
    private async Task Save()
    {
        var s = _settingsService.Current;
        s.GpuVariant = GpuVariant;
        s.OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
            ? AppSettings.DefaultOutputDirectory
            : OutputDirectory;
        s.ModelsDirectory = string.IsNullOrWhiteSpace(ModelsDirectory)
            ? AppSettings.DefaultModelsDirectory
            : ModelsDirectory;
        s.YtdlpPath = YtdlpPath;
        s.YtdlpCookiesFromBrowser = string.IsNullOrWhiteSpace(YtdlpCookiesFromBrowser)
            ? null
            : YtdlpCookiesFromBrowser;
        s.YtdlpJsRuntime = string.IsNullOrWhiteSpace(YtdlpJsRuntime) ? null : YtdlpJsRuntime;
        s.FirstRunComplete = true;

        await _settingsService.SaveAsync();

        SaveSuccess = true;
        await Task.Delay(2000);
        SaveSuccess = false;
    }
}

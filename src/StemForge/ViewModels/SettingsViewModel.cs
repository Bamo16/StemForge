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
    private bool _toolsLoading = true;

    [ObservableProperty]
    private bool _allSystemsGo;

    public ObservableCollection<ToolStatusViewModel> Tools { get; } = new();

    // ── GPU variant ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private GpuVariant _gpuVariant;

    public bool IsCpu       => GpuVariant == GpuVariant.Cpu;
    public bool IsCuda      => GpuVariant == GpuVariant.Cuda;
    public bool IsDirectML  => GpuVariant == GpuVariant.DirectML;

    // ── Directories ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _modelsDirectory = string.Empty;

    // ── Optional tool paths ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _ytdlpPath = string.Empty;

    [ObservableProperty]
    private string _ffmpegPath = string.Empty;

    // ── Save state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _saveSuccess;

    public SettingsViewModel(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromSettings(settingsService.Current);
    }

    private void LoadFromSettings(AppSettings s)
    {
        GpuVariant       = s.GpuVariant;
        OutputDirectory  = s.OutputDirectory;
        ModelsDirectory  = s.ModelsDirectory;
        YtdlpPath        = s.YtdlpPath  ?? string.Empty;
        FfmpegPath       = s.FfmpegPath ?? string.Empty;
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
        try
        {
            var results = await SetupDetector.DetectAllAsync(
                string.IsNullOrWhiteSpace(YtdlpPath)  ? null : YtdlpPath,
                string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath
            );
            foreach (var t in results)
                Tools.Add(new ToolStatusViewModel(t));

            AllSystemsGo = results.All(t => t.Found || !t.IsRequired);
        }
        finally
        {
            ToolsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshTools() => await DetectToolsAsync();

    [RelayCommand]
    private async Task Save()
    {
        var s = _settingsService.Current;
        s.GpuVariant      = GpuVariant;
        s.OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? AppSettings.DefaultOutputDirectory : OutputDirectory;
        s.ModelsDirectory = string.IsNullOrWhiteSpace(ModelsDirectory) ? AppSettings.DefaultModelsDirectory : ModelsDirectory;
        s.YtdlpPath       = string.IsNullOrWhiteSpace(YtdlpPath)  ? null : YtdlpPath;
        s.FfmpegPath      = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath;

        await _settingsService.SaveAsync();

        SaveSuccess = true;
        await Task.Delay(2000);
        SaveSuccess = false;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Single source of truth for which prerequisite tools are installed.
/// View-models bind to its observable properties so install/uninstall actions
/// anywhere in the app can drive a one-shot <see cref="RefreshAsync"/> and have
/// every page (Settings, Separate, Models) react automatically.
/// </summary>
public sealed partial class ToolStateService(SetupDetector detector, AppSettings settings)
    : ObservableObject
{
    private readonly SetupDetector _detector = detector;
    private readonly AppSettings _settings = settings;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ToolInfo> Tools { get; set; } = [];

    public bool IsUvAvailable => Find("uv")?.Found ?? false;
    public bool IsAudioSeparatorAvailable => Find("audio-separator")?.Found ?? false;
    public bool IsYtdlpAvailable => Find("yt-dlp")?.Found ?? false;
    public bool IsFfmpegAvailable => Find("ffmpeg")?.Found ?? false;
    public bool CanDownloadFromUrl => IsYtdlpAvailable && IsFfmpegAvailable;

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var ytdlpPath = string.IsNullOrWhiteSpace(_settings.YtdlpPath)
                ? null
                : _settings.YtdlpPath;
            Tools = await _detector.DetectAllAsync(ytdlpPath);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnToolsChanged(IReadOnlyList<ToolInfo> value)
    {
        OnPropertyChanged(nameof(IsUvAvailable));
        OnPropertyChanged(nameof(IsAudioSeparatorAvailable));
        OnPropertyChanged(nameof(IsYtdlpAvailable));
        OnPropertyChanged(nameof(IsFfmpegAvailable));
        OnPropertyChanged(nameof(CanDownloadFromUrl));
    }

    private ToolInfo? Find(string name) => Tools.FirstOrDefault(t => t.Name == name);
}

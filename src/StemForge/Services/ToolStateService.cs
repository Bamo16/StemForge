using CommunityToolkit.Mvvm.ComponentModel;

namespace StemForge.Services;

/// <summary>
/// Single source of truth for which prerequisite tools are installed.
/// View-models bind to its observable properties so install/uninstall actions
/// anywhere in the app can drive a one-shot <see cref="RefreshAsync"/> and have
/// every page (Settings, Separate, Models) react automatically.
/// </summary>
public sealed partial class ToolStateService(SetupDetector detector) : ObservableObject
{
    private readonly SetupDetector _detector = detector;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ToolInfo> Tools { get; private set; } = [];

    public bool IsUvAvailable => IsAvailable("uv");
    public bool IsAudioSeparatorAvailable => IsAvailable("audio-separator");
    public bool IsYtdlpAvailable => IsAvailable("yt-dlp");
    public bool IsFfmpegAvailable => IsAvailable("ffmpeg");
    public bool CanDownloadFromUrl => IsYtdlpAvailable && IsFfmpegAvailable;

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            Tools = await _detector.DetectAllAsync();
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

    private bool IsAvailable(string name) =>
        Tools.FirstOrDefault(t => t.Name == name)?.Found ?? false;
}

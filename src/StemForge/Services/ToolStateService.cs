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
    public bool IsDenoAvailable => IsAvailable("deno");
    public bool CanDownloadFromUrl => IsYtdlpAvailable && IsFfmpegAvailable;

    /// <summary>
    /// Re-detect tool availability. Pass no arguments to refresh all four tools; pass one
    /// or more tool names to refresh only that subset (the others stay as they were).
    /// If Tools is empty (no prior detection), always falls back to a full refresh so
    /// subsequent reads don't see stale "not found" entries for tools we never checked.
    /// </summary>
    public async Task RefreshAsync(params string[] toolNames)
    {
        IsLoading = true;
        try
        {
            if (toolNames.Length == 0 || Tools.Count == 0)
            {
                Tools = await _detector.DetectAllAsync();
                return;
            }

            var updated = await _detector.DetectAsync(toolNames);
            Tools = [.. Tools.Select(t => updated.FirstOrDefault(u => u.Name == t.Name) ?? t)];
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
        OnPropertyChanged(nameof(IsDenoAvailable));
        OnPropertyChanged(nameof(CanDownloadFromUrl));
    }

    private bool IsAvailable(string name) =>
        Tools.FirstOrDefault(t => t.Name == name)?.Found ?? false;
}

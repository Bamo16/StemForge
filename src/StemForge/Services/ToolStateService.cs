using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Single source of truth for which prerequisite tools are installed. State is held keyed by
/// <see cref="ToolKind"/> and surfaced as an ordered <see cref="Tools"/> list for binding.
/// View-models bind to its observable properties so install/uninstall actions anywhere in the app
/// can drive a one-shot <see cref="RefreshAsync"/> and have every page (Settings, Separate, Models)
/// react automatically.
/// </summary>
public sealed partial class ToolStateService(SetupDetector detector) : ObservableObject
{
    private readonly SetupDetector _detector = detector;

    // Authoritative store keyed by ToolKind. Tools is a projection of this, in catalog order.
    private Dictionary<ToolKind, ToolState> _byKind = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ToolState> Tools { get; private set; } = [];

    public bool IsAvailable(ToolKind kind) =>
        _byKind.TryGetValue(kind, out var state) && state.Found;

    public bool IsUvAvailable => IsAvailable(ToolKind.Uv);
    public bool IsAudioSeparatorAvailable => IsAvailable(ToolKind.AudioSeparator);
    public bool IsYtdlpAvailable => IsAvailable(ToolKind.Ytdlp);
    public bool IsFfmpegAvailable => IsAvailable(ToolKind.Ffmpeg);
    public bool IsDenoAvailable => IsAvailable(ToolKind.Deno);
    public bool CanDownloadFromUrl => IsYtdlpAvailable && IsFfmpegAvailable;

    /// <summary>
    /// Re-detect tool availability. Pass no arguments to refresh every tool; pass one or more
    /// <see cref="ToolKind"/>s to refresh only that subset (the others stay as they were). If no
    /// prior detection has run, always falls back to a full refresh so subsequent reads don't see
    /// stale "not found" entries for tools we never checked.
    /// </summary>
    public async Task RefreshAsync(params ToolKind[] kinds)
    {
        IsLoading = true;
        try
        {
            if (kinds.Length == 0 || _byKind.Count == 0)
            {
                Apply(await _detector.DetectAsync());
                return;
            }

            var updated = await _detector.DetectAsync(kinds);
            var merged = new Dictionary<ToolKind, ToolState>(_byKind);
            foreach (var state in updated)
                merged[state.Kind] = state;
            Apply(merged.Values);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(IEnumerable<ToolState> states)
    {
        _byKind = states.ToDictionary(s => s.Kind);
        // Project to a list in catalog order so binding consumers get a stable, grouped order.
        Tools = [.. ToolCatalog.All.Select(t => _byKind[t.Kind])];
    }

    partial void OnToolsChanged(IReadOnlyList<ToolState> value)
    {
        OnPropertyChanged(nameof(IsUvAvailable));
        OnPropertyChanged(nameof(IsAudioSeparatorAvailable));
        OnPropertyChanged(nameof(IsYtdlpAvailable));
        OnPropertyChanged(nameof(IsFfmpegAvailable));
        OnPropertyChanged(nameof(IsDenoAvailable));
        OnPropertyChanged(nameof(CanDownloadFromUrl));
    }
}

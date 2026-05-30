using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

/// <summary>
/// One tool's row on the Settings page. Holds the live detection state (found, version,
/// optional variant tag) consumed by the status header, and the user's path-override string
/// consumed by the tool-paths section. The settings view-model owns the collection and keeps
/// the rows in sync with <see cref="ToolStateService"/> and <see cref="AppSettings"/>.
/// </summary>
public sealed partial class SettingsToolRowViewModel(Tool tool) : ObservableObject
{
    public ToolKind Kind => tool.Kind;
    public string Name => tool.CliName;
    public bool IsRequired => tool.IsRequired;

    /// <summary>Placeholder text describing the default discovery when no override is set.</summary>
    public string PathPlaceholder =>
        Kind switch
        {
            ToolKind.Uv => "Default: 'uv' on PATH",
            ToolKind.AudioSeparator =>
                "Default: uv-installed shim, falls back to 'audio-separator' on PATH",
            ToolKind.Ytdlp =>
                "Default: bundled binary in %LOCALAPPDATA%\\StemForge\\bin, falls back to 'yt-dlp' on PATH",
            ToolKind.Ffmpeg =>
                "Default: bundled binary in %LOCALAPPDATA%\\StemForge\\bin, falls back to 'ffmpeg' on PATH",
            ToolKind.Deno =>
                "Default: bundled binary in %LOCALAPPDATA%\\StemForge\\bin, falls back to 'deno' on PATH",
            _ => string.Empty,
        };

    [ObservableProperty]
    public partial bool Found { get; set; }

    [ObservableProperty]
    public partial string Version { get; set; } = string.Empty;

    /// <summary>Currently set only for the audio-separator row; null elsewhere.</summary>
    [ObservableProperty]
    public partial string? VariantTag { get; set; }

    /// <summary>User override path for this tool, two-way bound to the input field.</summary>
    [ObservableProperty]
    public partial string PathOverride { get; set; } = string.Empty;

    /// <summary>Status text shown under each row in the header: version, or a "not found" hint.</summary>
    public string StatusLine =>
        Found ? Version : (IsRequired ? "Not found" : "Not found (optional)");

    partial void OnFoundChanged(bool value) => OnPropertyChanged(nameof(StatusLine));

    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(StatusLine));
}

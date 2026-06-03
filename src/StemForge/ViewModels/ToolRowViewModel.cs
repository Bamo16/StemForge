using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

/// <summary>
/// One tool's row in the setup wizard install step. Holds install-flow state for a single tool;
/// the wizard owns the collection and drives the actual installs. Replaces the per-tool
/// property quintuplets that were previously duplicated five times on the wizard view-model.
/// </summary>
public sealed partial class ToolRowViewModel(Tool tool, IVariantPicker? variantPicker = null)
    : ObservableObject
{
    public ToolKind Kind => tool.Kind;
    public string Name => tool.CliName;
    public string Description => tool.Description;
    public string? DownloadSize => tool.DownloadSize;
    public bool HasDownloadSize => !string.IsNullOrWhiteSpace(tool.DownloadSize);
    public bool IsRequired => tool.IsRequired;

    /// <summary>Wizard-supplied variant picker for tools that have variants; null otherwise.</summary>
    public IVariantPicker? VariantPicker { get; } = variantPicker;

    [ObservableProperty]
    public partial bool Found { get; set; }

    [ObservableProperty]
    public partial bool WantInstall { get; set; }

    [ObservableProperty]
    public partial bool InstallSucceeded { get; set; }

    [ObservableProperty]
    public partial string? InstallError { get; set; }

    /// <summary>True while this tool is actively installing; drives the indeterminate progress row.</summary>
    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    /// <summary>
    /// Message shown beside the indeterminate progress bar while installing (e.g. a heads-up that
    /// audio-separator can take several minutes). Empty when there is nothing extra to say.
    /// </summary>
    [ObservableProperty]
    public partial string InProgressMessage { get; set; } = string.Empty;

    /// <summary>Found and installed during this wizard run (vs already present at detect time).</summary>
    public bool ShowInstalledNow => Found && InstallSucceeded;

    /// <summary>Found but was already present when the wizard checked.</summary>
    public bool ShowAlreadyInstalled => Found && !InstallSucceeded;

    /// <summary>Show the inline variant picker only while this tool is queued for install and idle.</summary>
    public bool ShowVariantPicker =>
        VariantPicker is not null && WantInstall && !Found && !IsInstalling;

    partial void OnFoundChanged(bool value)
    {
        RaiseOutcome();
        OnPropertyChanged(nameof(ShowVariantPicker));
    }

    partial void OnInstallSucceededChanged(bool value) => RaiseOutcome();

    partial void OnWantInstallChanged(bool value) => OnPropertyChanged(nameof(ShowVariantPicker));

    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(ShowVariantPicker));

    private void RaiseOutcome()
    {
        OnPropertyChanged(nameof(ShowInstalledNow));
        OnPropertyChanged(nameof(ShowAlreadyInstalled));
    }
}

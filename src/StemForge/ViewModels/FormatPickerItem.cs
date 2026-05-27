using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;

namespace StemForge.ViewModels;

public partial class FormatPickerItem : ObservableObject
{
    public required YtDlpFormat Format { get; init; }
    public string FormatId => Format.FormatId ?? "";
    public required string Codec { get; init; }
    public required string Bitrate { get; init; }
    public required string SampleRate { get; init; }
    public string FormatNote { get; init; } = "";
    public bool IsAutoRecommended { get; init; }
    public bool IsYtPremium { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

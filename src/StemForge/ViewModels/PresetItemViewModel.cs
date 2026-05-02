using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;

namespace StemForge.ViewModels;

public partial class PresetItemViewModel(Preset preset) : ObservableObject
{
    public Preset Preset { get; } = preset;

    public string Id => Preset.Id;
    public string Label => Preset.Label;
    public string Description => Preset.Description;
    public string ModelsTag =>
        Preset.Mode == SeparationMode.SingleModel ? "1 model"
        : Preset.ModelCount > 0 ? $"{Preset.ModelCount} models"
        : string.Empty;
    public string VramTag => Preset.Vram;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Models;

namespace StemForge.ViewModels;

public partial class PresetItemViewModel : ObservableObject
{
    public Preset Preset { get; }

    public PresetItemViewModel(Preset preset)
    {
        Preset = preset;
    }

    public string Id => Preset.Id;
    public string Label => Preset.Label;
    public string Description => Preset.Description;
    public string ModelsTag => $"{Preset.ModelCount} models";
    public string VramTag => Preset.Vram;

    [ObservableProperty]
    private bool _isSelected;
}

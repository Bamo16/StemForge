using System.Collections.ObjectModel;
using Avalonia.Media;
using StemForge.Models;

namespace StemForge.ViewModels;

public sealed class PresetCategoryGroup
{
    public PresetCategory Category { get; }
    public string Label { get; }
    public IBrush AccentBrush { get; }
    public ObservableCollection<PresetItemViewModel> Items { get; }

    public PresetCategoryGroup(PresetCategory category, IBrush accentBrush, IEnumerable<PresetItemViewModel> items)
    {
        Category = category;
        Label = category switch
        {
            PresetCategory.Vocals => "VOCALS",
            PresetCategory.Instrumentals => "INSTRUMENTALS",
            PresetCategory.Other => "OTHER",
            _ => category.ToString().ToUpperInvariant(),
        };
        AccentBrush = accentBrush;
        Items = new ObservableCollection<PresetItemViewModel>(items);
    }
}

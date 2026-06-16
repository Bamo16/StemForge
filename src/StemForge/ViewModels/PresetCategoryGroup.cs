using System.Collections.ObjectModel;
using Avalonia.Media;
using StemForge.Core.Models;

namespace StemForge.ViewModels;

public sealed class PresetCategoryGroup(
    PresetCategory category,
    IBrush accentBrush,
    IEnumerable<PresetItemViewModel> items
)
{
    public PresetCategory Category { get; } = category;
    public string Label { get; } =
        category switch
        {
            PresetCategory.Vocals => "VOCALS",
            PresetCategory.Instrumentals => "INSTRUMENTALS",
            PresetCategory.Other => "OTHER",
            _ => category.ToString().ToUpperInvariant(),
        };
    public IBrush AccentBrush { get; } = accentBrush;
    public ObservableCollection<PresetItemViewModel> Items { get; } =
        new ObservableCollection<PresetItemViewModel>(items);
}

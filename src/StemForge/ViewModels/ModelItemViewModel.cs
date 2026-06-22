using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;

namespace StemForge.ViewModels;

public partial class ModelItemViewModel(ModelInfo model) : ObservableObject
{
    public ModelInfo Model { get; } = model;

    public string Filename => Model.Filename;
    public string FriendlyName => Model.FriendlyName;
    public string Architecture => Model.Architecture;
    public IReadOnlyList<StemSdr> Stems => Model.Stems;

    public string StemNames =>
        Model.Stems.Count > 0
            ? string.Join(
                ", ",
                Model.Stems.Select(s => s.Sdr.HasValue ? $"{s.Name} ({s.Sdr.Value:F1})" : s.Name)
            )
            : string.Empty;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsLocal { get; set; }

    [ObservableProperty]
    public partial long FileSizeBytes { get; set; }

    public string FileSizeDisplay =>
        FileSizeBytes <= 0 ? string.Empty : FileSizeBytes.Bytes().Humanize("0.#");
}

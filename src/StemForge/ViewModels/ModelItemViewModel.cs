using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;

namespace StemForge.ViewModels;

public partial class ModelItemViewModel(ModelInfo model, ModelProfile? profile = null)
    : ObservableObject
{
    public ModelInfo Model { get; } = model;

    /// <summary>
    /// Advisory profile resolved without running the model (architecture defaults, filename target,
    /// or fetched config). Used to surface stems for models the benchmark data lists none for.
    /// </summary>
    public ModelProfile? Profile { get; } = profile;

    public string Filename => Model.Filename;
    public string FriendlyName => Model.FriendlyName;
    public string Architecture => Model.Architecture;
    public IReadOnlyList<StemSdr> Stems => Model.Stems;

    /// <summary>
    /// The stems to display: the benchmark/config stems (with SDR) when present, otherwise the
    /// resolved profile stems (no SDR — they are inferred, not measured). Empty when even the
    /// profile is UNKNOWN.
    /// </summary>
    public string StemNames
    {
        get
        {
            if (Model.Stems.Count > 0)
            {
                return string.Join(
                    ", ",
                    Model.Stems.Select(s =>
                        s.Sdr.HasValue ? $"{s.Name} ({s.Sdr.Value:F1})" : s.Name
                    )
                );
            }

            if (Profile is { IsUnknown: false })
                return string.Join(", ", Profile.Stems.Select(s => s.Name));

            return string.Empty;
        }
    }

    /// <summary>
    /// True when the displayed stems are advisory (resolved from the profile, not measured), so the
    /// UI can mark them as inferred rather than benchmark-backed.
    /// </summary>
    public bool StemsAreInferred => Model.Stems.Count == 0 && Profile is { IsUnknown: false };

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsLocal { get; set; }

    [ObservableProperty]
    public partial long FileSizeBytes { get; set; }

    public string FileSizeDisplay =>
        FileSizeBytes <= 0 ? string.Empty : FileSizeBytes.Bytes().Humanize("0.#");
}

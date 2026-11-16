using System.Globalization;

namespace StemForge.ViewModels;

/// <summary>One stem line in the details flyout; <paramref name="Sdr"/> is empty when not measured.</summary>
public sealed record StemDetail(string Name, string Sdr, bool IsTarget);

/// <summary>Where audio-separator fetches a model's weights from.</summary>
public sealed record DownloadSource(string Label, string Url);

/// <summary>The facts the Models view's details flyout shows for one model, all derived from catalog data.</summary>
public sealed class ModelDetails(
    ModelInfo model,
    ModelProfile? profile,
    IEnumerable<Preset> presets,
    ModelNote? note
)
{
    private const string UvrReleases =
        "https://github.com/TRvlvr/model_repo/releases/tag/all_public_uvr_models";
    private const string DemucsFiles = "https://dl.fbaipublicfiles.com/demucs/";

    public ModelNote? Note { get; } = note;
    public bool HasNote => Note is not null;
    public IReadOnlyList<ModelNoteLink> NoteLinks => Note?.Links ?? [];

    public string Filename => model.Filename;
    public string Architecture => model.Architecture;

    public DownloadSource Source { get; } =
        model.Architecture == "Demucs"
            ? new DownloadSource("dl.fbaipublicfiles.com/demucs", DemucsFiles)
            : new DownloadSource("github.com/TRvlvr/model_repo releases", UvrReleases);

    /// <summary>Benchmark stems with SDR, else the resolved profile's stems (no SDR, inferred).</summary>
    public IReadOnlyList<StemDetail> Stems { get; } =
        model.Stems.Count > 0
            ?
            [
                .. model.Stems.Select(stem => new StemDetail(
                    stem.Name,
                    stem.Sdr is { } sdr ? $"{sdr:F1} dB" : string.Empty,
                    IsTarget(stem.Name, model.TargetStem)
                )),
            ]
        : profile is { IsUnknown: false }
            ?
            [
                .. profile.Stems.Select(stem => new StemDetail(
                    stem.Name,
                    string.Empty,
                    IsTarget(stem.Name, model.TargetStem)
                )),
            ]
        : [];

    public bool HasStems => Stems.Count > 0;

    public string? Speed { get; } =
        model.SecondsPerMinute is { } seconds
            ? $"{seconds.ToString("0.0#", CultureInfo.InvariantCulture)} seconds per minute of audio, measured on the benchmark machine"
            : null;

    public string? Training { get; } =
        CheckpointName.TryParse(model.Filename, out var name)
            ? $"Epoch {name.Epoch}, author's validation SDR {name.Sdr.ToString("0.0#", CultureInfo.InvariantCulture)}"
            : null;

    /// <summary>Labels of the built-in presets that run this model, in catalog order.</summary>
    public IReadOnlyList<string> PresetLabels { get; } =
    [
        .. presets
            .Where(preset =>
                preset.AllModels.Contains(model.Filename, StringComparer.OrdinalIgnoreCase)
            )
            .Select(preset => $"{preset.Label} ({preset.Category})"),
    ];

    public string? PresetsText =>
        PresetLabels is { Count: > 0 } labels ? string.Join(", ", labels) : null;

    private static bool IsTarget(string stem, string? target) =>
        target is { Length: > 0 } && stem.Equals(target, StringComparison.OrdinalIgnoreCase);
}

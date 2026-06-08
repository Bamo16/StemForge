namespace StemForge.Core.Models;

public enum SeparationMode
{
    BuiltinPreset,
    SingleModel,
    CustomEnsemble,
}

public enum PresetCategory
{
    Vocals,
    Instrumentals,
    Drums,
    Bass,
    Guitar,
    Piano,
    Other,
}

/// <param name="PrimaryModel">BuiltinPreset: null → use Id as preset name. SingleModel / CustomEnsemble: the primary model filename.</param>
/// <param name="Models">BuiltinPreset: model filenames from the driver catalog. Custom modes: derived from PrimaryModel + ExtraModels.</param>
public sealed record Preset(
    string Id,
    string Label,
    PresetCategory Category,
    string Description,
    int ModelCount,
    string Vram,
    IReadOnlyList<string>? Models = null,
    SeparationMode Mode = SeparationMode.BuiltinPreset,
    string? PrimaryModel = null,
    string? EnsembleAlgorithm = null,
    IReadOnlyList<string>? ExtraModels = null,
    IReadOnlyList<double>? EnsembleWeights = null
)
{
    /// <summary>All model filenames for this preset, regardless of mode.</summary>
    public IReadOnlyList<string> AllModels =>
        Models is { Count: > 0 } m ? m
        : PrimaryModel is not null ? [PrimaryModel, .. ExtraModels ?? []]
        : [];

    /// <summary>
    /// Human-readable preset name shown to users and embedded in output provenance tags
    /// (e.g. "Instrumental - Full", "Karaoke"). Built-in presets are qualified by their
    /// category; custom modes use the user-supplied label as-is.
    /// </summary>
    public string DisplayName =>
        Mode != SeparationMode.BuiltinPreset ? Label
        : Id == "karaoke" ? "Karaoke"
        : Category switch
        {
            PresetCategory.Vocals => $"Vocal - {Label}",
            PresetCategory.Instrumentals => $"Instrumental - {Label}",
            _ => $"{Category} - {Label}",
        };

    /// <summary>
    /// The single-model preset used by the "Add drum stems to output" step. Drum extraction is a
    /// configured single-model pass rather than a catalog ensemble, so it is modelled here as a
    /// first-class <see cref="SeparationMode.SingleModel"/> preset in the Drums category. It carries
    /// the model for per-model provenance and yields a "Drums - {model}" display name consistent
    /// with how separation presets tag their outputs, replacing the descriptor that was previously
    /// synthesized inline at the tagging call site.
    /// </summary>
    public static Preset DrumExtraction(string modelFilename) =>
        new(
            Id: "drums",
            Label: $"Drums - {Path.GetFileNameWithoutExtension(modelFilename)}",
            Category: PresetCategory.Drums,
            Description: "Drum stem extraction",
            ModelCount: 1,
            Vram: "",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: modelFilename
        );
}

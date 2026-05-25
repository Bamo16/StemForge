namespace StemForge.Models;

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
}

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
public sealed record Preset(
    string Id,
    string Label,
    PresetCategory Category,
    string Description,
    int ModelCount,
    string Vram,
    SeparationMode Mode = SeparationMode.BuiltinPreset,
    string? PrimaryModel = null,
    string? EnsembleAlgorithm = null,
    IReadOnlyList<string>? ExtraModels = null,
    IReadOnlyList<double>? EnsembleWeights = null
);

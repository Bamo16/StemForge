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

public sealed record Preset(
    string Id,
    string Label,
    PresetCategory Category,
    string Description,
    int ModelCount,
    string Vram,
    SeparationMode Mode = SeparationMode.BuiltinPreset,
    // BuiltinPreset: null → use Id as preset name
    // SingleModel / CustomEnsemble: the primary model filename
    string? PrimaryModel = null,
    string? EnsembleAlgorithm = null,
    IReadOnlyList<string>? ExtraModels = null,
    IReadOnlyList<double>? EnsembleWeights = null
);

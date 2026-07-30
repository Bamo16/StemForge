namespace StemForge.Core.Catalog;

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

/// <summary>
/// A named separation recipe. Structurally a preset is an ordered list of <see cref="PresetStep"/>s;
/// today every preset is exactly one step, with multi-step (chained) presets a deferred extension.
/// The persisted schema (user_presets.json) already stores the steps list, so chaining needs no
/// future migration.
///
/// The flat constructor parameters (<paramref name="PrimaryModel"/>, <paramref name="ExtraModels"/>,
/// <paramref name="EnsembleAlgorithm"/>) describe the preset's single step and remain the entry
/// point for built-in and UI-created presets. They are projected into a one-element
/// <see cref="Steps"/> list and re-exposed as computed properties so existing consumers and the
/// pipeline see exactly the same shape as before.
/// </summary>
/// <param name="PrimaryModel">BuiltinPreset: null → use Id as preset name. SingleModel / CustomEnsemble: the primary model filename.</param>
/// <param name="Models">BuiltinPreset: model filenames from the driver catalog. Custom modes: derived from PrimaryModel + ExtraModels.</param>
/// <param name="Steps">
/// The ordered steps that make up this preset. When null (built-in and UI call sites), a single
/// step is derived from the flat model/algorithm parameters. Persistence always supplies this so
/// the on-disk shape is the source of truth.
/// </param>
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
    IReadOnlyList<double>? EnsembleWeights = null,
    IReadOnlyList<PresetStep>? Steps = null,
    string? NameTemplate = null
)
{
    /// <summary>
    /// The ordered steps of this preset. Always at least one element. When not supplied explicitly,
    /// a single step is synthesized from the flat model/algorithm parameters so built-in and UI
    /// call sites need not change. Built-in presets carry no model list of their own here (the
    /// driver owns the definition), which yields an empty model list on the synthesized step.
    /// </summary>
    public IReadOnlyList<PresetStep> Steps { get; init; } =
        Steps is { Count: > 0 } supplied
            ? supplied
            :
            [
                new PresetStep(
                    StepInput.Source,
                    Models: Mode switch
                    {
                        SeparationMode.SingleModel => PrimaryModel is not null
                            ? [PrimaryModel]
                            : [],
                        SeparationMode.CustomEnsemble => PrimaryModel is not null
                            ? [PrimaryModel, .. ExtraModels ?? []]
                            : [],
                        _ => Models ?? [],
                    },
                    Algorithm: EnsembleAlgorithm,
                    KeepSet: null,
                    NameTemplate: NameTemplate
                ),
            ];

    /// <summary>
    /// The optional output-name template that drives this preset's output file names, or null to use
    /// the clean "title (stem)" default. Read from the terminal step (the step that writes the files
    /// the user keeps); for a single-step preset that is the only step. See
    /// <see cref="StemForge.Core.Separation.OutputNamer"/> for the token set.
    /// </summary>
    public string? OutputNameTemplate => Steps[^1].NameTemplate;

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

namespace StemForge.Core.Catalog;

/// <summary>
/// Where a <see cref="PresetStep"/> takes its audio from. Today every step reads the job's source
/// audio; the enum exists so chained (multi-step) presets can later name an earlier step's output
/// stem as input without changing the persisted schema.
/// </summary>
public enum StepInput
{
    /// <summary>The job's source audio (a downloaded URL or a local file). The only kind today.</summary>
    Source,
}

/// <summary>
/// A single stage of a <see cref="Preset"/>'s recipe: an input, the model-or-ensemble to run, the
/// ensemble algorithm (when two or more models run together), and the keep set. A preset is an
/// ordered list of steps; today every preset is exactly one step whose input is always the source
/// audio. The container is introduced now so chained (multi-step) presets need no future schema
/// migration.
/// </summary>
/// <param name="Input">Where this step reads its audio. <see cref="StepInput.Source"/> today.</param>
/// <param name="Models">
/// The model-or-ensemble to run: one filename for a single-model step, two or more for an ensemble.
/// </param>
/// <param name="Algorithm">
/// The ensemble algorithm used to combine outputs when <see cref="Models"/> holds two or more
/// models. Null for a single-model step.
/// </param>
/// <param name="KeepSet">
/// The output stems this step retains; the rest are discarded after the run. An empty set means
/// "keep everything the run emits" (the prior default for user presets), so the keep set never has
/// to enumerate stems the model profile cannot name yet.
/// </param>
/// <param name="NameTemplate">
/// Optional output-name template for the stems this step writes. When set, it drives each output's
/// file name via the <c>title</c>, <c>stem</c>, and <c>preset</c> tokens (see
/// <see cref="StemForge.Core.Separation.OutputNamer"/>); when null/empty, naming falls back to the
/// clean "title (stem)" default. Lives on the step because the step is the unit that emits stems; a
/// future chained preset names each step's outputs independently, with the terminal step naming the
/// files the user ultimately keeps.
/// </param>
public sealed record PresetStep(
    StepInput Input,
    IReadOnlyList<string> Models,
    string? Algorithm = null,
    IReadOnlyList<string>? KeepSet = null,
    string? NameTemplate = null
)
{
    /// <summary>True when this step runs two or more models combined by <see cref="Algorithm"/>.</summary>
    public bool IsEnsemble => Models.Count >= 2;
}

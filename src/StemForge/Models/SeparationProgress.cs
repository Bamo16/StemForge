namespace StemForge.Models;

/// <summary>Progress snapshot emitted during a separation job.</summary>
/// <param name="PresetIndex">0-based index of the preset currently running.</param>
/// <param name="PresetCount">Total number of presets in this job.</param>
/// <param name="PresetLabel">Human-readable preset name (e.g. "Vocals + Drums").</param>
/// <param name="StepLabel">Current operation: "Separating", "Loading model", "Creating ensemble", etc.</param>
/// <param name="StepPercent">Raw tqdm progress for the current step (0–100); 0 when the step is starting.</param>
public sealed record SeparationProgress(
    int PresetIndex,
    int PresetCount,
    string PresetLabel,
    string StepLabel,
    int StepPercent
);

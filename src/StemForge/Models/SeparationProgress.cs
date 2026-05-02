namespace StemForge.Models;

public sealed record SeparationProgress(
    int PresetIndex,    // 0-based index of the preset currently running
    int PresetCount,    // total number of presets in this job
    string PresetLabel, // human-readable preset name
    string StepLabel,   // current operation: "Separating", "Loading model", "Creating ensemble", etc.
    int StepPercent     // raw tqdm progress for the current step (0–100); 0 when step is starting
);

using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Built-in preset catalog. IDs match the <c>audio-separator</c> ensemble preset names
/// (see BamoUtils/Internal/AudioSeparatorPresets.cs fallback list). Once audio-separator
/// is installed internally we'll load <c>ensemble_presets.json</c> and merge real model
/// counts; for now these match the hi-fi mock.
/// </summary>
public sealed class PresetCatalog
{
    public static IReadOnlyList<Preset> BuiltIn { get; } =
    [
        new("vocal_balanced",          "Balanced", PresetCategory.Vocals,        "Best all-round vocal quality",              4, "6 GB"),
        new("vocal_clean",             "Clean",    PresetCategory.Vocals,        "Minimal instrument bleed",                  3, "4 GB"),
        new("vocal_full",              "Full",     PresetCategory.Vocals,        "Maximum capture, harmonies included",       5, "8 GB"),
        new("vocal_rvc",               "RVC / AI", PresetCategory.Vocals,        "Optimized for voice training datasets",     3, "4 GB"),
        new("instrumental_balanced",   "Balanced", PresetCategory.Instrumentals, "Good balance of noise and fullness",        4, "6 GB"),
        new("instrumental_clean",      "Clean",    PresetCategory.Instrumentals, "Cleanest output, minimal vocal residue",    3, "4 GB"),
        new("instrumental_full",       "Full",     PresetCategory.Instrumentals, "Maximum instrument preservation",           5, "8 GB"),
        new("instrumental_low_resource","Low VRAM",PresetCategory.Instrumentals, "Fast ensemble for weak GPUs",               2, "2 GB"),
        new("karaoke",                 "Karaoke",  PresetCategory.Other,         "Remove lead vocal, keep everything else",   2, "2 GB"),
    ];
}

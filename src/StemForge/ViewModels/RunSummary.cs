using Humanizer;

namespace StemForge.ViewModels;

/// <summary>
/// What a run from the Separate view will write, and how the footer says so. A run is any
/// non-empty combination of preset stems, a drum stem and the source audio.
/// </summary>
/// <param name="CanSeparate">False when audio-separator is missing: presets and the drum stem then count for nothing.</param>
public sealed record RunSummary(
    int PresetCount,
    int PresetModelCount,
    bool DrumStemTicked,
    bool SourceTicked,
    RunInput Input,
    bool CanSeparate
)
{
    public int Presets => CanSeparate ? PresetCount : 0;

    public bool Drums => CanSeparate && DrumStemTicked;

    /// <summary>A local file is already the source, so the tick counts only for a URL.</summary>
    public bool Source => SourceTicked && Input is RunInput.Url;

    public int ModelRuns => (CanSeparate ? PresetModelCount : 0) + (Drums ? 1 : 0);

    public bool HasOutput => Presets > 0 || Drums || Source;

    public bool ShowsPresetCount => Presets > 0;

    public bool IsSourceOnly => this is { Presets: 0, Drums: false, Source: true };

    /// <summary>True when the headline stands on its own rather than following the amber preset count.</summary>
    public bool IsHeadlineStandalone => this is { Presets: 0, HasOutput: true };

    /// <summary>The headline after the amber count, or the whole headline when there is no count.</summary>
    public string Headline =>
        this switch
        {
            // Keep today's wording for a plain preset run; the count is rendered separately.
            { Presets: > 0 and var count, Drums: false, Source: false } =>
                $" {"preset".ToQuantity(count, ShowQuantityAs.None)} selected",
            { Presets: > 0 and var count } => $" {"preset".ToQuantity(count, ShowQuantityAs.None)}"
                + (Drums ? " + drum stem" : string.Empty)
                + (Source ? " + source" : string.Empty),
            { Drums: true, Source: true } => "Drum stem + source",
            { Drums: true } => "Drum stem only",
            { Source: true } => "Source audio only",
            _ => "Nothing to write",
        };

    public string Detail =>
        this switch
        {
            { ModelRuns: > 0 and var runs } => "model run".ToQuantity(runs),
            { IsSourceOnly: true } => "Download only · no separation",
            { Input: RunInput.File } => "Pick a preset or Drum stem",
            { Input: RunInput.Url, CanSeparate: false } => "Tick Source audio to download",
            _ => "Pick a preset, Drum stem or Source",
        };

    public string PrimaryActionLabel => IsSourceOnly ? "↓  Download" : "▶  Run";
}

public enum RunInput
{
    None,
    File,
    Url,
}

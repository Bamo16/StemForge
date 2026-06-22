namespace StemForge.Core.Catalog;

/// <summary>
/// Where a profiled stem name came from, in DESCENDING order of confidence. The resolver records
/// the source per stem so the UI can show how sure StemForge is about a name without ever treating
/// it as authoritative — audio-separator remains the source of truth for what a model actually
/// emits (see ADR 0010).
/// </summary>
public enum StemSource
{
    /// <summary>No stem could be resolved at all.</summary>
    Unknown = 0,

    /// <summary>Inferred from the model's filename as a last resort (target only, no complement).</summary>
    FilenameTarget = 1,

    /// <summary>Filled from a fixed architecture default (Demucs four-stem; MDX/VR target + complement).</summary>
    ArchitectureDefault = 2,

    /// <summary>Taken from the model's own config / benchmark instrument list (highest confidence).</summary>
    Config = 3,
}

/// <summary>A single resolved output stem and the source it was resolved from.</summary>
public sealed record ProfileStem(string Name, StemSource Source);

/// <summary>
/// The set of facts StemForge derives about a <see cref="ModelInfo"/> WITHOUT running it: its
/// architecture, its output stems (each tagged with the <see cref="StemSource"/> it came from),
/// and whether it is a composite ("bag") model — one weight file that is internally several
/// sub-models (e.g. <c>htdemucs_ft</c>). The profile is ADVISORY: it informs stem-aware UI but
/// never blocks a separation.
/// </summary>
public sealed record ModelProfile(
    string Filename,
    string Architecture,
    IReadOnlyList<ProfileStem> Stems,
    bool IsComposite
)
{
    /// <summary>The lowest-confidence source present, or <see cref="StemSource.Unknown"/> when empty.
    /// Used to summarise overall confidence for a model in one value.</summary>
    public StemSource Confidence =>
        Stems.Count == 0 ? StemSource.Unknown : Stems.Min(s => s.Source);

    /// <summary>True when no output stems could be resolved by any tier.</summary>
    public bool IsUnknown => Stems.Count == 0;
}

namespace StemForge.Core.Models;

/// <summary>
/// Progress snapshot emitted during a single driver <c>run</c> command.
/// The <see cref="Phase"/> field identifies which driver event this came from;
/// the other fields are populated depending on the phase.
/// </summary>
public sealed record JobProgress
{
    /// <summary>
    /// One of: downloading_model, loading_model, separating, ensembling,
    /// stem_written, log. Matches the driver event/phase field.
    /// </summary>
    public string Phase { get; init; } = "";

    // -- downloading_model / loading_model --
    public string? Model { get; init; }
    public int? ModelIndex { get; init; }
    public int? ModelCount { get; init; }
    public bool? Cached { get; init; }

    // -- progress (inference bar) --
    public int? Current { get; init; }
    public int? Total { get; init; }
    public bool? Final { get; init; }

    // -- ensembling --
    public string? Stem { get; init; }

    // -- stem_written --
    public string? OutputStem { get; init; }
    public string? OutputPath { get; init; }

    // -- log --
    public string? LogLevel { get; init; }
    public string? LogMessage { get; init; }
}

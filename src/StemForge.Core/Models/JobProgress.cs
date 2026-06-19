namespace StemForge.Core.Models;

/// <summary>
/// The kind of driver event a <see cref="JobProgress"/> snapshot was produced from. This is the
/// transport-level discriminator (which event the driver emitted), kept distinct from the
/// <see cref="JobProgress.Phase"/> sub-state so the two are never conflated in a single string.
/// </summary>
public enum JobProgressKind
{
    /// <summary>A phase transition; <see cref="JobProgress.Phase"/> says which sub-state.</summary>
    Phase,

    /// <summary>An inference progress tick (current/total), rendered as a percentage.</summary>
    Progress,

    /// <summary>A forwarded structured log line from the separator.</summary>
    Log,

    /// <summary>One output stem was written to disk.</summary>
    StemWritten,
}

/// <summary>
/// A sub-state of the running separation, carried only when <see cref="JobProgress.Kind"/> is
/// <see cref="JobProgressKind.Phase"/>. The public mirror of the driver-internal phase enum; the
/// closed set is defined by the driver protocol (see <c>tools/driver_protocol.json</c>).
/// </summary>
public enum JobPhase
{
    DownloadingModel,
    LoadingModel,
    Separating,
    Ensembling,
}

/// <summary>
/// Progress snapshot emitted during a single driver <c>run</c> command.
/// <see cref="Kind"/> identifies which driver event produced this snapshot; <see cref="Phase"/>
/// (populated only for <see cref="JobProgressKind.Phase"/>) names the sub-state. The other fields
/// are populated depending on the kind/phase.
/// </summary>
public sealed record JobProgress
{
    /// <summary>Which driver event produced this snapshot.</summary>
    public JobProgressKind Kind { get; init; }

    /// <summary>
    /// The phase sub-state, populated only when <see cref="Kind"/> is
    /// <see cref="JobProgressKind.Phase"/>; null otherwise.
    /// </summary>
    public JobPhase? Phase { get; init; }

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

    /// <summary>
    /// The stable string label this snapshot maps to on <see cref="JobUpdate.Phase"/>, which carries
    /// both driver-derived and pipeline-level phases in one vocabulary. Kept as the single point of
    /// translation so the typed driver shape and the broader pipeline phase string stay in sync.
    /// </summary>
    public string UpdatePhase =>
        Kind switch
        {
            JobProgressKind.Phase => Phase switch
            {
                JobPhase.DownloadingModel => "downloading_model",
                JobPhase.LoadingModel => "loading_model",
                JobPhase.Separating => "separating",
                JobPhase.Ensembling => "ensembling",
                _ => "",
            },
            JobProgressKind.Progress => "progress",
            JobProgressKind.Log => "log",
            JobProgressKind.StemWritten => "stem_written",
            _ => "",
        };
}

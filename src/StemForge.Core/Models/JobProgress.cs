namespace StemForge.Core.Models;

/// <summary>
/// A sub-state of the running separation, carried on a <see cref="PhaseProgress"/>. The public
/// mirror of the driver-internal phase enum; the closed set is defined by the driver protocol (see
/// <c>tools/driver_protocol.json</c>).
/// </summary>
public enum JobPhase
{
    DownloadingModel,
    LoadingModel,
    Separating,
    Ensembling,
}

/// <summary>
/// A progress snapshot emitted during a single driver <c>run</c>. One derived record per kind of
/// driver event the dispatcher forwards; each carries only the fields meaningful for that event, so
/// there is no single record with mutually-exclusive nullable fields. <see cref="UpdatePhase"/> is
/// the stable label this snapshot maps onto <see cref="JobUpdate.Phase"/>.
/// </summary>
public abstract record JobProgress
{
    /// <summary>The stable string label this snapshot contributes to <see cref="JobUpdate.Phase"/>.</summary>
    public abstract string UpdatePhase { get; }
}

/// <summary>A phase transition; <see cref="Phase"/> names the sub-state.</summary>
public sealed record PhaseProgress(JobPhase Phase) : JobProgress
{
    // downloading_model / loading_model
    public string? Model { get; init; }
    public int? ModelIndex { get; init; }
    public int? ModelCount { get; init; }
    public bool? Cached { get; init; }

    // ensembling target stem; separating carries ModelCount only
    public string? Stem { get; init; }

    public override string UpdatePhase =>
        Phase switch
        {
            JobPhase.DownloadingModel => "downloading_model",
            JobPhase.LoadingModel => "loading_model",
            JobPhase.Separating => "separating",
            JobPhase.Ensembling => "ensembling",
            _ => "",
        };
}

/// <summary>An inference progress tick (current/total), rendered as a percentage.</summary>
public sealed record ProgressTick : JobProgress
{
    public int? Current { get; init; }
    public int? Total { get; init; }
    public bool? Final { get; init; }

    public override string UpdatePhase => "progress";
}

/// <summary>A forwarded structured log line from the separator.</summary>
public sealed record LogLine : JobProgress
{
    public string? Level { get; init; }
    public string? Message { get; init; }

    public override string UpdatePhase => "log";
}

/// <summary>One output stem was written to disk.</summary>
public sealed record StemWritten : JobProgress
{
    public string? Stem { get; init; }
    public string? Path { get; init; }

    public override string UpdatePhase => "stem_written";
}

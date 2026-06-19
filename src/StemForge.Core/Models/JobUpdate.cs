namespace StemForge.Core.Models;

/// <summary>
/// A single progress snapshot emitted by <c>SeparationPipeline.RunAsync</c>.
/// The <see cref="Phase"/> field identifies the pipeline event; other fields are
/// populated depending on the phase.
/// </summary>
public sealed record JobUpdate
{
    /// <summary>Overall job completion percentage, 0-100, already computed by the pipeline.</summary>
    public int OverallPercent { get; init; }

    /// <summary>
    /// Semantic phase. Matches <see cref="JobProgress.UpdatePhase"/> driver values where applicable,
    /// plus pipeline-level phases: "starting", "downloading", "tagging", "keep_source",
    /// "run_complete", "done", "failed", "cancelled".
    /// </summary>
    public string Phase { get; init; } = "";

    /// <summary>Zero-based index of the separation run this update belongs to.</summary>
    public int RunIndex { get; init; }

    /// <summary>Total number of separation runs for this job.</summary>
    public int RunCount { get; init; }

    /// <summary>Human-readable label for this run (preset label or "Drums").</summary>
    public string? RunLabel { get; init; }

    // ── Model-level detail forwarded from JobProgress ─────────────────────────

    public string? Model { get; init; }
    public int? ModelIndex { get; init; }
    public int? ModelCount { get; init; }

    // ── Ensembling ────────────────────────────────────────────────────────────

    /// <summary>Stem being combined (ensembling phase).</summary>
    public string? Stem { get; init; }

    // ── Inference progress ────────────────────────────────────────────────────

    public int? ProgressCurrent { get; init; }
    public int? ProgressTotal { get; init; }
    public bool? ProgressFinal { get; init; }

    // ── Downloading-model / cached status ────────────────────────────────────

    public bool? Cached { get; init; }

    // ── run_complete ──────────────────────────────────────────────────────────

    /// <summary>Output file paths written during this run (emitted once per run at run_complete).</summary>
    public IReadOnlyList<string>? WrittenPaths { get; init; }

    // ── stem_written ──────────────────────────────────────────────────────────

    public string? OutputPath { get; init; }

    // ── log ───────────────────────────────────────────────────────────────────

    public string? LogMessage { get; init; }
    public string? LogLevel { get; init; }

    // ── Terminal status ───────────────────────────────────────────────────────

    /// <summary>True when Phase is "done", "failed", or "cancelled".</summary>
    public bool IsTerminal { get; init; }

    public string? ErrorMessage { get; init; }
}

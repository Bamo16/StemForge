namespace StemForge.Core.Separation.Models;

public sealed record JobResult(
    bool Succeeded,
    IReadOnlyList<JobOutput> Outputs,
    IReadOnlyList<JobOutput> Discarded,
    double DurationSeconds,
    string? ErrorMessage,
    string? Traceback
);

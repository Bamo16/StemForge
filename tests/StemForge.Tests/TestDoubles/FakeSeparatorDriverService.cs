namespace StemForge.Tests.TestDoubles;

/// <summary>
/// Hand-written test double for ISeparatorDriverService.
/// Configure NextResult and ProgressReporter before calling RunAsync.
/// </summary>
public sealed class FakeSeparatorDriverService : ISeparatorDriverService
{
    public JobResult NextResult { get; set; } = new(true, [], [], 0, null, null);

    public IProgress<JobProgress>? ProgressReporter { get; set; }

    public Task<JobResult> RunAsync(
        JobRequest request,
        IProgress<JobProgress>? progress,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        ProgressReporter?.Report(new PhaseProgress(JobPhase.Separating));
        progress?.Report(new PhaseProgress(JobPhase.Separating));
        return Task.FromResult(NextResult);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

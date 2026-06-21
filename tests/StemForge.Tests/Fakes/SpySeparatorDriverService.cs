using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Fakes;

/// <summary>
/// Configurable test spy for <see cref="ISeparatorDriverService"/>. Records all received
/// requests and returns pre-configured results with optional progress sequences.
/// </summary>
public sealed class SpySeparatorDriverService : ISeparatorDriverService
{
    public sealed record RunConfig(
        JobResult Result,
        IReadOnlyList<JobProgress>? ProgressSequence = null
    );

    private readonly Queue<RunConfig> _runs = new();

    /// <summary>Requests received by RunAsync in call order.</summary>
    public List<JobRequest> ReceivedRequests { get; } = [];

    public int CallCount => ReceivedRequests.Count;

    /// <summary>Enqueue a run that returns <paramref name="result"/> with no progress events.</summary>
    public void EnqueueRun(JobResult result) => _runs.Enqueue(new RunConfig(result));

    /// <summary>Enqueue a run that emits <paramref name="progress"/> events then returns <paramref name="result"/>.</summary>
    public void EnqueueRun(JobResult result, IReadOnlyList<JobProgress> progress) =>
        _runs.Enqueue(new RunConfig(result, progress));

    public Task<JobResult> RunAsync(
        JobRequest request,
        IProgress<JobProgress>? progress,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        ReceivedRequests.Add(request);

        if (_runs.Count == 0)
            throw new InvalidOperationException(
                "SpySeparatorDriverService: no more runs configured."
            );

        var run = _runs.Dequeue();

        if (run.ProgressSequence is not null)
            foreach (var p in run.ProgressSequence)
                progress?.Report(p);

        return Task.FromResult(run.Result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

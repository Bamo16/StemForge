using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Fakes;

/// <summary>
/// Hand-written test double for ISeparatorDriverService.
/// Configure NextResult and ProgressReporter before calling RunAsync.
/// </summary>
public sealed class FakeSeparatorDriverService : ISeparatorDriverService
{
    public JobResult NextResult { get; set; } = new(true, [], [], 0, null, null);

    public IProgress<JobProgress>? ProgressReporter { get; set; }

    public event Action<IReadOnlyList<Preset>>? PresetsLoaded;

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

    public void RaisePresetsLoaded(IReadOnlyList<Preset> presets) => PresetsLoaded?.Invoke(presets);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

using StemForge.Models;

namespace StemForge.Services;

public interface ISeparatorDriverService : IAsyncDisposable
{
    /// <summary>
    /// Run one separation job. Spawns the driver process on first call (or
    /// after an idle-timeout teardown) and waits for <c>ready</c> before sending
    /// the command. Cancelling <paramref name="ct"/> kills the process and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<JobResult> RunAsync(
        JobRequest request,
        IProgress<JobProgress>? progress,
        CancellationToken ct
    );
}

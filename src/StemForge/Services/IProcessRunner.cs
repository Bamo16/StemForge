namespace StemForge.Services;

public interface IProcessRunner
{
    Task<ProcessRunner.Result> RunAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default
    );

    Task<ProcessRunner.Result> RunCheckedAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default
    );

    Task RunStreamingAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? progress = null,
        CancellationToken ct = default
    );
}

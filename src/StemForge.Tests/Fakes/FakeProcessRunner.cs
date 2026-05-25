using StemForge.Services;

namespace StemForge.Tests.Fakes;

/// <summary>
/// Configurable fake for IProcessRunner. Register responses keyed by exe name.
/// Unregistered exes return exit-code 1 (not-found) by default.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public record FakeResult(int ExitCode = 0, string Stdout = "", string Stderr = "");

    private readonly Dictionary<string, FakeResult> _responses = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<(string Exe, IReadOnlyList<string> Args)> _calls = [];

    public IReadOnlyList<(string Exe, IReadOnlyList<string> Args)> Calls => _calls;

    public void Setup(string exe, FakeResult result) => _responses[exe] = result;

    public void Setup(string exe, string stdout, int exitCode = 0) =>
        _responses[exe] = new FakeResult(exitCode, stdout);

    public Task<ProcessRunner.Result> RunAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default,
        bool logRawLines = true
    )
    {
        var argList = args.ToList();
        _calls.Add((exe, argList));
        var fake = Resolve(exe);
        return Task.FromResult(new ProcessRunner.Result(fake.ExitCode, fake.Stdout, fake.Stderr));
    }

    public Task<ProcessRunner.Result> RunCheckedAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct = default,
        bool logRawLines = true
    )
    {
        var argList = args.ToList();
        _calls.Add((exe, argList));
        var fake = Resolve(exe);
        if (fake.ExitCode != 0)
            throw new ProcessRunner.ProcessFailedException(exe, fake.ExitCode, fake.Stderr);
        return Task.FromResult(new ProcessRunner.Result(fake.ExitCode, fake.Stdout, fake.Stderr));
    }

    public Task RunStreamingAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        bool logRawLines = true
    )
    {
        var argList = args.ToList();
        _calls.Add((exe, argList));
        var fake = Resolve(exe);

        if (!string.IsNullOrEmpty(fake.Stdout))
            foreach (var line in fake.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                progress?.Report(line);

        if (fake.ExitCode != 0)
            throw new ProcessRunner.ProcessFailedException(exe, fake.ExitCode, fake.Stderr);

        return Task.CompletedTask;
    }

    public Task<ProcessRunner.Result> RunStreamingStderrAsync(
        string exe,
        IEnumerable<string> args,
        IProgress<string>? stderrProgress = null,
        CancellationToken ct = default,
        bool logRawLines = true
    )
    {
        var argList = args.ToList();
        _calls.Add((exe, argList));
        var fake = Resolve(exe);

        if (!string.IsNullOrEmpty(fake.Stderr))
            foreach (var line in fake.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                stderrProgress?.Report(line);

        if (fake.ExitCode != 0)
            throw new ProcessRunner.ProcessFailedException(exe, fake.ExitCode, fake.Stderr);

        return Task.FromResult(new ProcessRunner.Result(fake.ExitCode, fake.Stdout, fake.Stderr));
    }

    private FakeResult Resolve(string exe)
    {
        if (_responses.TryGetValue(exe, out var r))
            return r;
        throw new InvalidOperationException($"FakeProcessRunner: no setup for exe '{exe}'");
    }
}

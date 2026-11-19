namespace StemForge.Tests.TestDoubles;

/// <summary>
/// Configurable fake for IProcessRunner. Register responses keyed by fileName name.
/// Unregistered exes return exit-code 1 (not-found) by default.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public record FakeResult(int ExitCode = 0, string Stdout = "", string Stderr = "");

    private readonly Dictionary<string, FakeResult> _responses =
    [
        with(StringComparer.OrdinalIgnoreCase),
    ];
    private readonly List<(string Exe, IReadOnlyList<string> Args)> _calls = [];

    public IReadOnlyList<(string Exe, IReadOnlyList<string> Args)> Calls => _calls;

    public void Setup(string fileName, FakeResult result) => _responses[fileName] = result;

    public void Setup(string fileName, string stdout, int exitCode = 0) =>
        _responses[fileName] = new FakeResult(exitCode, stdout);

    public Task<ProcessRunner.Result> RunAsync(
        string fileName,
        IEnumerable<string> args,
        bool logRawLines = true,
        CancellationToken ct = default
    )
    {
        var argList = args.ToList();
        _calls.Add((fileName, argList));
        var fake = Resolve(fileName);
        return Task.FromResult(new ProcessRunner.Result(fake.ExitCode, fake.Stdout, fake.Stderr));
    }

    public Task<ProcessRunner.Result> RunCheckedAsync(
        string fileName,
        IEnumerable<string> args,
        bool logRawLines = true,
        CancellationToken ct = default
    )
    {
        var argList = args.ToList();
        _calls.Add((fileName, argList));
        var fake = Resolve(fileName);
        if (fake.ExitCode != 0)
            throw new ProcessRunner.ProcessFailedException(fileName, fake.ExitCode, fake.Stderr);
        return Task.FromResult(new ProcessRunner.Result(fake.ExitCode, fake.Stdout, fake.Stderr));
    }

    public Task RunStreamingAsync(
        string fileName,
        IEnumerable<string> args,
        IProgress<string>? progress = null,
        bool logRawLines = true,
        CancellationToken ct = default
    )
    {
        var argList = args.ToList();
        _calls.Add((fileName, argList));
        var fake = Resolve(fileName);

        if (!string.IsNullOrEmpty(fake.Stdout))
            foreach (var line in fake.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                progress?.Report(line);

        if (fake.ExitCode != 0)
            throw new ProcessRunner.ProcessFailedException(fileName, fake.ExitCode, fake.Stderr);

        return Task.CompletedTask;
    }

    public Task<ProcessRunner.Result> RunStreamingStderrAsync(
        string fileName,
        IEnumerable<string> args,
        IProgress<string>? stderrProgress = null,
        bool logRawLines = true,
        CancellationToken ct = default
    )
    {
        var argList = args.ToList();
        _calls.Add((fileName, argList));
        var fake = Resolve(fileName);

        if (!string.IsNullOrEmpty(fake.Stderr))
            foreach (var line in fake.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                stderrProgress?.Report(line);

        if (fake.ExitCode != 0)
            throw new ProcessRunner.ProcessFailedException(fileName, fake.ExitCode, fake.Stderr);

        return Task.FromResult(new ProcessRunner.Result(fake.ExitCode, fake.Stdout, fake.Stderr));
    }

    /// <summary>Records the producer's call, then the consumer's, and fails as the producer does
    /// first, as the real runner does.</summary>
    public Task RunPipedAsync(
        string producer,
        IEnumerable<string> producerArgs,
        string consumer,
        IEnumerable<string> consumerArgs,
        IProgress<string>? progress = null,
        CancellationToken ct = default
    )
    {
        _calls.Add((producer, producerArgs.ToList()));
        _calls.Add((consumer, consumerArgs.ToList()));

        foreach (var fileName in (string[])[producer, consumer])
        {
            if (Resolve(fileName) is { ExitCode: not 0 } fake)
                throw new ProcessRunner.ProcessFailedException(
                    fileName,
                    fake.ExitCode,
                    fake.Stderr
                );
        }

        return Task.CompletedTask;
    }

    private FakeResult Resolve(string fileName)
    {
        if (_responses.TryGetValue(fileName, out var r))
            return r;
        throw new InvalidOperationException($"FakeProcessRunner: no setup for '{fileName}'");
    }
}

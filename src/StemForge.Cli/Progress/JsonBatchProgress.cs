namespace StemForge.Cli.Progress;

/// <summary>
/// Silent progress surface for <c>--json</c> mode. Stdout must carry only the final JSON payload,
/// so per-input progress and completion lines are dropped entirely; commands collect their own
/// structured results and serialize them once the batch finishes. Warnings and errors still go to
/// stderr so failures remain visible without corrupting stdout.
/// </summary>
internal sealed class JsonBatchProgress : IBatchProgress
{
    public Task RunAsync(int totalInputs, Func<Task> body) => body();

    public IInputProgress BeginInput(int index, int total, string label) =>
        new SilentInputProgress();

    public void Log(LogLevel level, string source, string message)
    {
        if (level is LogLevel.Warning or LogLevel.Error)
            Console.Error.WriteLine($"[{Tag(level)}] {source}: {message}");
    }

    private static string Tag(LogLevel level) =>
        level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???",
        };

    private sealed class SilentInputProgress : IInputProgress
    {
        public void Report(int overallPercent, string? activity) { }

        public void Complete(InputOutcome outcome, string? message) { }

        public void Dispose() { }
    }
}

namespace StemForge.Cli.Progress;

/// <summary>
/// Line-based progress for non-interactive or redirected sessions. Emits a single line per input
/// at start and completion, plus periodic percentage lines, so logs and CI output stay clean and
/// free of ANSI control sequences.
///
/// Verbose mode (which also forces this renderer on an interactive terminal, see
/// <see cref="BatchProgressFactory"/>) additionally streams every log line, and emits a single
/// milestone line per phase/model transition instead of periodic percentage ticks: with the driver
/// logs already on screen, percent spam would only bury them. Non-verbose redirected sessions keep
/// the periodic percentage lines, which are their only progress signal.
/// </summary>
internal sealed class PlainBatchProgress(
    TextWriter standardOut,
    TextWriter standardError,
    bool verbose
) : IBatchProgress
{
    public Task RunAsync(int totalInputs, Func<Task> body) => body();

    public IInputProgress BeginInput(int index, int total, string label) =>
        new PlainInputProgress(standardOut, standardError, index + 1, total, label, verbose);

    public void Log(LogLevel level, string source, string message)
    {
        if (level is LogLevel.Warning or LogLevel.Error)
        {
            standardError.WriteLine($"[{Tag(level)}] {source}: {message}");
            return;
        }

        if (verbose)
            standardOut.WriteLine($"[{Tag(level)}] {source}: {message}");
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

    private sealed class PlainInputProgress(
        TextWriter standardOut,
        TextWriter standardError,
        int jobNum,
        int total,
        string label,
        bool verbose
    ) : IInputProgress
    {
        private int _lastReportedPercent = -1;
        private string? _lastActivity;
        private bool _announced;

        public void Report(int overallPercent, string? activity)
        {
            // Announce the input once, on the first update.
            if (!_announced)
            {
                standardOut.WriteLine($"[{jobNum}/{total}] {label}");
                _announced = true;
            }

            // A milestone is any phase/model transition (the activity text changed). In verbose
            // that is the only thing that prints a progress line: the percentage ticks are dropped
            // so the streamed driver logs are not buried under percent spam. In non-verbose plain
            // (redirected/CI) the periodic percentage line is the only progress signal, so it is
            // also printed when the percent advances by a visible step.
            var activityChanged = activity is not null && activity != _lastActivity;
            var percentAdvanced =
                !verbose && overallPercent >= 0 && overallPercent / 10 != _lastReportedPercent / 10;

            if (activityChanged || percentAdvanced)
            {
                var shown = activity ?? _lastActivity ?? "Working";
                standardOut.WriteLine($"[{jobNum}/{total}] {overallPercent, 3}%  {shown}");
                _lastReportedPercent = overallPercent;
                if (activity is not null)
                    _lastActivity = activity;
            }
        }

        public void Complete(InputOutcome outcome, string? message)
        {
            switch (outcome)
            {
                case InputOutcome.Succeeded:
                    standardOut.WriteLine($"[{jobNum}/{total}] Done. {message ?? label}");
                    break;
                case InputOutcome.Failed:
                    standardError.WriteLine($"[{jobNum}/{total}] Error: {message ?? "failed"}");
                    break;
                case InputOutcome.Cancelled:
                    standardError.WriteLine($"[{jobNum}/{total}] Cancelled.");
                    break;
            }
        }

        public void Dispose() { }
    }
}

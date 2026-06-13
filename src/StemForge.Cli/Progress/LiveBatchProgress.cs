using Spectre.Console;
using StemForge.Core.Services;

namespace StemForge.Cli.Progress;

/// <summary>
/// Live terminal progress using Spectre.Console's <see cref="Progress"/> display. Each input gets a
/// single overall bar (driven by the pipeline's job-wide percentage) plus a fixed-width status
/// column to its left, so the bar never shifts as the status text changes length. A batch of more
/// than one input also gets a parent bar. The input's title is printed once above the bars when it
/// starts; log lines stream above the display without corrupting it, and non-error logs appear only
/// when verbose is enabled.
/// </summary>
internal sealed class LiveBatchProgress(IAnsiConsole console, bool verbose) : IBatchProgress
{
    // Fixed status-column width. The status string is padded or truncated to exactly this many
    // characters so the bar column starts at the same position on every frame.
    private const int StatusWidth = 46;

    private ProgressContext? _ctx;
    private ProgressTask? _parent;
    private int _completedInputs;

    public async Task RunAsync(int totalInputs, Func<Task> body)
    {
        await console
            .Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async ctx =>
            {
                _ctx = ctx;
                _parent =
                    totalInputs > 1
                        ? ctx.AddTask(
                            Status($"Batch (0/{totalInputs})"),
                            new ProgressTaskSettings { MaxValue = totalInputs }
                        )
                        : null;

                await body();

                if (_parent is { } parent)
                    parent.Value = parent.MaxValue;
            });
    }

    public IInputProgress BeginInput(int index, int total, string label)
    {
        // Defensive: BeginInput is only valid inside RunAsync's body.
        var ctx =
            _ctx
            ?? throw new InvalidOperationException(
                "BeginInput called outside of an active progress display."
            );

        // Announce the input's title once, scrolling in above the live bars. The bars themselves
        // then only carry the (fixed-width) status, so a long title never widens the bar row.
        var indexTag = total > 1 ? $"[grey][[{index + 1}/{total}]][/] " : "";
        console.MarkupLine($"{indexTag}[bold]{Markup.Escape(label)}[/]");

        var prefix = total > 1 ? $"[{index + 1}/{total}] " : "";
        var task = ctx.AddTask(
            Status($"{prefix}Starting"),
            new ProgressTaskSettings { MaxValue = 100 }
        );

        return new LiveInputProgress(this, task, prefix);
    }

    public void Log(LogLevel level, string source, string message)
    {
        if (level is not (LogLevel.Warning or LogLevel.Error) && !verbose)
            return;

        var color = level switch
        {
            LogLevel.Error => "red",
            LogLevel.Warning => "yellow",
            LogLevel.Debug => "grey",
            _ => "blue",
        };
        var tag = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???",
        };

        // [[ and ]] are Spectre's escape for literal brackets; a single [WRN] would be parsed as a
        // (nonexistent) style and throw. Writing from inside StartAsync scrolls the line in above
        // the live bars.
        console.MarkupLine(
            $"[{color}][[{tag}]][/] [grey]{Markup.Escape(source)}:[/] {Markup.Escape(message)}"
        );
    }

    private void OnInputCompleted()
    {
        _completedInputs++;
        if (_parent is { } parent)
        {
            parent.Value = _completedInputs;
            parent.Description = Status($"Batch ({_completedInputs}/{(int)parent.MaxValue})");
        }
    }

    /// <summary>Pads or truncates plain status text to a fixed width and escapes it for markup.</summary>
    private static string Status(string plain, string? color = null)
    {
        var sized =
            plain.Length > StatusWidth
                ? string.Concat(plain.AsSpan(0, StatusWidth - 1), "…")
                : plain.PadRight(StatusWidth);
        var escaped = Markup.Escape(sized);
        return color is null ? escaped : $"[{color}]{escaped}[/]";
    }

    private sealed class LiveInputProgress(
        LiveBatchProgress owner,
        ProgressTask task,
        string prefix
    ) : IInputProgress
    {
        private bool _completed;
        private string _lastStatus = "Starting";

        public void Report(int overallPercent, string? activity)
        {
            // The reporter already feeds a monotonic percentage; Max is a cheap belt-and-braces
            // guard so a stray lower value can never drag the single bar backwards.
            if (overallPercent >= 0)
                task.Value = Math.Max(task.Value, Math.Clamp(overallPercent, 0, 100));

            if (activity is { Length: > 0 })
                _lastStatus = activity;

            task.Description = Status($"{prefix}{_lastStatus}");
        }

        public void Complete(InputOutcome outcome, string? message)
        {
            if (_completed)
                return;
            _completed = true;

            var (mark, color) = outcome switch
            {
                InputOutcome.Succeeded => ("Done", "green"),
                InputOutcome.Failed => ("Error", "red"),
                InputOutcome.Cancelled => ("Cancelled", "yellow"),
                _ => ("", "grey"),
            };

            if (outcome == InputOutcome.Succeeded)
                task.Value = 100;

            var text = message is { Length: > 0 } ? $"{mark}: {message}" : mark;
            task.Description = Status($"{prefix}{text}", color);
            task.StopTask();
            owner.OnInputCompleted();
        }

        public void Dispose()
        {
            // Ensure the task is stopped even if Complete was never called (e.g. an exception
            // escaped before the outcome was known).
            if (!_completed)
            {
                task.StopTask();
                owner.OnInputCompleted();
            }
        }
    }
}

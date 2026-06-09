using Spectre.Console;
using StemForge.Core.Services;

namespace StemForge.Cli.Progress;

/// <summary>
/// Live terminal progress using Spectre.Console's <see cref="Progress"/> display. Shows a parent
/// bar for the batch and a per-input bar, each with a percentage and a current-activity
/// description. Log lines are written above the live display without corrupting it; non-error
/// logs are shown only when verbose is enabled.
/// </summary>
internal sealed class LiveBatchProgress(IAnsiConsole console, bool verbose) : IBatchProgress
{
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
                            $"[bold]Batch[/] (0/{totalInputs})",
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

        var safeLabel = Markup.Escape(label);
        var prefix = total > 1 ? $"[grey]\\[{index + 1}/{total}][/] " : "";
        var task = ctx.AddTask($"{prefix}{safeLabel}", new ProgressTaskSettings { MaxValue = 100 });

        return new LiveInputProgress(this, task, safeLabel, prefix);
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

        // Writing from within the StartAsync body scrolls the line in above the live bars.
        console.MarkupLine(
            $"[{color}]\\[{tag}][/] [grey]{Markup.Escape(source)}:[/] {Markup.Escape(message)}"
        );
    }

    private void OnInputCompleted()
    {
        _completedInputs++;
        if (_parent is { } parent)
        {
            parent.Value = _completedInputs;
            parent.Description = $"[bold]Batch[/] ({_completedInputs}/{(int)parent.MaxValue})";
        }
    }

    private sealed class LiveInputProgress(
        LiveBatchProgress owner,
        ProgressTask task,
        string label,
        string prefix
    ) : IInputProgress
    {
        private bool _completed;

        public void Report(int overallPercent, string? activity)
        {
            if (overallPercent >= 0)
                task.Value = Math.Clamp(overallPercent, 0, 100);

            if (activity is { Length: > 0 })
                task.Description = $"{prefix}{label} [grey]- {Markup.Escape(activity)}[/]";
        }

        public void Complete(InputOutcome outcome, string? message)
        {
            if (_completed)
                return;
            _completed = true;

            var (mark, color) = outcome switch
            {
                InputOutcome.Succeeded => ("done", "green"),
                InputOutcome.Failed => ("failed", "red"),
                InputOutcome.Cancelled => ("cancelled", "yellow"),
                _ => ("", "grey"),
            };

            if (outcome == InputOutcome.Succeeded)
                task.Value = 100;

            var suffix = message is { Length: > 0 } ? Markup.Escape(message) : mark;
            task.Description = $"{prefix}{label} [{color}]- {suffix}[/]";
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

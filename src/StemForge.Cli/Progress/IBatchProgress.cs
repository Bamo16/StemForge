namespace StemForge.Cli.Progress;

/// <summary>
/// A progress surface for a batch of inputs. Implementations render either a live terminal
/// display (parent bar plus per-input bar) or plain line output for non-interactive or
/// redirected sessions.
///
/// Lifecycle: call <see cref="RunAsync"/> with a callback that processes each input through an
/// <see cref="IInputProgress"/> obtained from <see cref="BeginInput"/>. The display is torn down
/// cleanly when the callback returns or throws.
/// </summary>
internal interface IBatchProgress
{
    /// <summary>
    /// Runs <paramref name="body"/> with the live display active. The body drives the batch loop;
    /// it obtains a per-input handle from <see cref="BeginInput"/> for each input it processes.
    /// </summary>
    Task RunAsync(int totalInputs, Func<Task> body);

    /// <summary>Begins a per-input progress handle for the input at <paramref name="index"/> (zero-based).</summary>
    IInputProgress BeginInput(int index, int total, string label);

    /// <summary>Writes a log line above the display. Honors the verbose setting for non-error levels.</summary>
    void Log(LogLevel level, string source, string message);
}

/// <summary>
/// A per-input progress handle. The command routes each <c>JobUpdate</c> through
/// <see cref="Report"/>; the handle updates its bar and current-activity text. Disposing marks
/// the input as finished (the caller passes the outcome via <see cref="Complete"/> first).
/// </summary>
internal interface IInputProgress : IDisposable
{
    /// <summary>Updates the per-input bar percentage and activity text from a pipeline update.</summary>
    void Report(int overallPercent, string? activity);

    /// <summary>Marks this input finished and advances the parent bar.</summary>
    void Complete(InputOutcome outcome, string? message);
}

internal enum InputOutcome
{
    Succeeded,
    Failed,
    Cancelled,
}

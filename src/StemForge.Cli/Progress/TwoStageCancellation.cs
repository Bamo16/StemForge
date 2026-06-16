namespace StemForge.Cli.Progress;

/// <summary>
/// Wires two-stage Ctrl+C handling onto a <see cref="CancellationTokenSource"/>. The first press
/// requests graceful cancellation (the in-flight work observes the token and tears down cleanly).
/// A second press forces an immediate process exit, bypassing any remaining teardown.
///
/// Dispose detaches the handler so it does not outlive the command.
/// </summary>
internal sealed class TwoStageCancellation : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Action<string> _notify;
    private readonly ConsoleCancelEventHandler _handler;
    private int _pressCount;

    private TwoStageCancellation(CancellationTokenSource cts, Action<string> notify)
    {
        _cts = cts;
        _notify = notify;
        _handler = OnCancelKeyPress;
        Console.CancelKeyPress += _handler;
    }

    /// <summary>Installs the handler. <paramref name="notify"/> reports each stage to the user.</summary>
    internal static TwoStageCancellation Install(
        CancellationTokenSource cts,
        Action<string> notify
    ) => new(cts, notify);

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        var count = Interlocked.Increment(ref _pressCount);
        if (count == 1)
        {
            // First press: cancel gracefully and keep the process alive for teardown.
            e.Cancel = true;
            _notify("Cancelling. Press Ctrl+C again to force exit.");
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
        else
        {
            // Second press: force an immediate exit. Do not set e.Cancel so the runtime is free
            // to terminate, and exit explicitly to guarantee a prompt stop.
            _notify("Forcing exit.");
            Environment.Exit(130); // 128 + SIGINT
        }
    }

    public void Dispose() => Console.CancelKeyPress -= _handler;
}

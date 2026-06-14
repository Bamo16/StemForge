using StemForge.Core.Services;

namespace StemForge.Cli.Progress;

/// <summary>
/// Routes <see cref="AppLogger"/> entries to whichever <see cref="IBatchProgress"/> is active, so
/// log lines render above the live display instead of corrupting it. When no display is active the
/// bridge falls back to writing warnings and errors to standard error, matching the prior behavior.
///
/// Register the sink once at startup with <see cref="RegisterSink"/>; activate and deactivate it
/// around each batch with <see cref="Activate"/>.
/// </summary>
internal static class ProgressLogBridge
{
    private static IBatchProgress? _active;
    private static readonly object _gate = new();

    /// <summary>Registers the single AppLogger sink that forwards to the active display.</summary>
    internal static void RegisterSink()
    {
        AppLogger.RegisterSink(entry =>
        {
            IBatchProgress? target;
            lock (_gate)
                target = _active;

            if (target is not null)
            {
                target.Log(entry.Level, entry.Source, entry.Message);
                return;
            }

            // No live display: preserve the prior warning/error-to-stderr behavior.
            if (entry.Level is LogLevel.Warning or LogLevel.Error)
                Console.Error.WriteLine($"[{entry.LevelTag}] {entry.Source}: {entry.Message}");
        });
    }

    /// <summary>
    /// Makes <paramref name="progress"/> the active log target until the returned scope is disposed.
    /// </summary>
    internal static IDisposable Activate(IBatchProgress progress)
    {
        lock (_gate)
            _active = progress;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            lock (_gate)
                _active = null;
        }
    }
}

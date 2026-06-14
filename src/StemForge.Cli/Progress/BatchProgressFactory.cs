using Spectre.Console;

namespace StemForge.Cli.Progress;

/// <summary>
/// Selects a batch progress implementation. A live Spectre display is used when the session is
/// interactive and not redirected; otherwise plain line output is used so logs and CI stay clean.
/// </summary>
internal static class BatchProgressFactory
{
    /// <summary>
    /// Returns true when a live progress display is appropriate: the Spectre profile reports an
    /// interactive, ANSI-capable terminal and neither standard stream is redirected.
    /// </summary>
    internal static bool ShouldUseLiveDisplay(IAnsiConsole console)
    {
        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
            return false;

        var caps = console.Profile.Capabilities;
        return caps.Interactive && caps.Ansi;
    }

    /// <summary>
    /// Creates the appropriate <see cref="IBatchProgress"/> for the current session.
    /// </summary>
    internal static IBatchProgress Create(IAnsiConsole console, bool verbose) =>
        ShouldUseLiveDisplay(console)
            ? new LiveBatchProgress(console, verbose)
            : new PlainBatchProgress(Console.Out, Console.Error, verbose);
}

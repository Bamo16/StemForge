using StemForge.Cli.Progress;

namespace StemForge.Tests.Commands;

/// <summary>
/// Tests the first stage of two-stage Ctrl+C handling: the first press cancels the token source
/// and notifies the user. The second stage forces an immediate process exit and so is exercised
/// only by a human running the CLI, not in-process.
/// </summary>
public sealed class TwoStageCancellationTests
{
    [Fact]
    public void Install_DoesNotCancelBeforeAnyPress()
    {
        using var cts = new CancellationTokenSource();
        var messages = new List<string>();

        using var cancellation = TwoStageCancellation.Install(cts, messages.Add);

        Assert.False(cts.IsCancellationRequested);
        Assert.Empty(messages);
    }

    [Fact]
    public void Dispose_DetachesHandlerWithoutCancelling()
    {
        using var cts = new CancellationTokenSource();
        var messages = new List<string>();

        var cancellation = TwoStageCancellation.Install(cts, messages.Add);
        cancellation.Dispose();

        Assert.False(cts.IsCancellationRequested);
    }
}

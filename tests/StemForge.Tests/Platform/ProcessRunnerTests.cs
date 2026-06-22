using System.Diagnostics;

namespace StemForge.Tests.Platform;

/// <summary>
/// Tests for <see cref="ProcessRunner"/>. These exercise the real spawn path against actual OS
/// commands, guarding the two behaviours touched by issue #62:
///   1. Setting <see cref="ProcessStartInfo.KillOnParentExit"/> on Windows/Linux must not break
///      the normal capture path (regression guard for the new ProcessStartInfo mutation).
///   2. The token-driven Kill(entireProcessTree: true) still reaps a running child promptly. That
///      path is the cooperative-cancellation route on every platform and the sole orphan-cleanup
///      route on macOS, where KillOnParentExit is unsupported.
///
/// The OS-level parent-exit reaping that KillOnParentExit provides (job object on Windows,
/// PR_SET_PDEATHSIG on Linux) is intentionally not asserted here: it triggers only when the
/// *parent* process dies, which in-process would mean killing the test runner. Verifying it
/// would require a separate host executable, which is out of scope for this change.
/// </summary>
public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task RunAsync_CapturesStdout_AfterKillOnParentExitWiring()
    {
        var (exe, args) = EchoCommand("stemforge-marker");

        var result = await _runner.RunAsync(exe, args, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Stderr);
        Assert.Contains("stemforge-marker", result.Output);
    }

    [Fact]
    public async Task RunAsync_CancellationKillsChild_Promptly()
    {
        // A long sleep is cancelled mid-flight; the child must be killed, not waited out.
        var (exe, args) = SleepCommand(seconds: 30);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(250));

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _runner.RunAsync(exe, args, ct: cts.Token)
        );
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    // ── Platform-agnostic command builders ───────────────────────────────────────

    private static (string Exe, string[] Args) EchoCommand(string text) =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", "echo", text])
            : ("/bin/sh", ["-c", $"echo {text}"]);

    private static (string Exe, string[] Args) SleepCommand(int seconds) =>
        OperatingSystem.IsWindows()
            // ping delays reliably under redirected stdio (timeout.exe errors without a console).
            ? ("cmd.exe", ["/c", "ping", "-n", (seconds + 1).ToString(), "127.0.0.1"])
            : ("/bin/sh", ["-c", $"sleep {seconds}"]);
}

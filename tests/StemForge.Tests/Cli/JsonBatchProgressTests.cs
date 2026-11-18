using StemForge.Cli.Progress;

namespace StemForge.Tests.Cli;

// Console.Error is process-wide, so these share a collection and never run beside each other.
[Collection("ConsoleError")]
public sealed class JsonBatchProgressTests
{
    private static string CaptureStderr(Action action)
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return captured.ToString();
    }

    [Theory]
    [InlineData(LogLevel.Warning, "[WRN] sftests2-warn: slow disk")]
    [InlineData(LogLevel.Error, "[ERR] sftests2-warn: slow disk")]
    public void Log_WarningOrError_GoesToStderrWithItsTag(LogLevel level, string expected)
    {
        var text = CaptureStderr(() =>
            new JsonBatchProgress().Log(level, "sftests2-warn", "slow disk")
        );

        Assert.Contains(expected, text);
    }

    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    public void Log_DebugOrInfo_WritesNothingSoStdoutStaysOnePayload(LogLevel level)
    {
        var text = CaptureStderr(() =>
            new JsonBatchProgress().Log(level, "sftests2-quiet", "chatter")
        );

        Assert.DoesNotContain("sftests2-quiet", text);
    }

    [Fact]
    public async Task RunAsync_RunsTheBodyAndHandsBackItsFailure()
    {
        var ran = false;

        await new JsonBatchProgress().RunAsync(
            1,
            () =>
            {
                ran = true;

                return Task.CompletedTask;
            }
        );

        Assert.True(ran);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new JsonBatchProgress().RunAsync(1, () => throw new InvalidOperationException("boom"))
        );
    }
}

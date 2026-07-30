using Spectre.Console;
using StemForge.Cli.Progress;

namespace StemForge.Tests.Commands;

/// <summary>
/// Tests the non-interactive progress surface used when output is redirected or the terminal is
/// not interactive: per-input lines, completion lines, the verbose log gate, and the activity
/// description mapping that feeds both the plain and live displays.
/// </summary>
public sealed class PlainBatchProgressTests
{
    private static (PlainBatchProgress Display, StringWriter Out, StringWriter Err) Build(
        bool verbose
    )
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var display = new PlainBatchProgress(outWriter, errWriter, verbose);
        return (display, outWriter, errWriter);
    }

    // ── Per-input announcement and completion ─────────────────────────────────

    [Fact]
    public async Task BeginInput_AnnouncesLabelAndReportsActivity()
    {
        var (display, outWriter, _) = Build(verbose: false);

        await display.RunAsync(
            1,
            () =>
            {
                using var input = display.BeginInput(0, 1, "song.flac");
                input.Report(10, "Downloading source");
                input.Complete(InputOutcome.Succeeded, "3 file(s)");
                return Task.CompletedTask;
            }
        );

        var output = outWriter.ToString();
        Assert.Contains("song.flac", output);
        Assert.Contains("Downloading source", output);
        Assert.Contains("Done.", output);
    }

    [Fact]
    public async Task FailedInput_WritesErrorToStandardError()
    {
        var (display, _, errWriter) = Build(verbose: false);

        await display.RunAsync(
            1,
            () =>
            {
                using var input = display.BeginInput(0, 1, "bad.flac");
                input.Complete(InputOutcome.Failed, "Input file not found");
                return Task.CompletedTask;
            }
        );

        Assert.Contains("Error", errWriter.ToString());
        Assert.Contains("Input file not found", errWriter.ToString());
    }

    [Fact]
    public async Task BatchPrefix_IncludesInputIndexAndTotal()
    {
        var (display, outWriter, _) = Build(verbose: false);

        await display.RunAsync(
            2,
            () =>
            {
                using var first = display.BeginInput(0, 2, "a.flac");
                first.Report(5, "Starting");
                using var second = display.BeginInput(1, 2, "b.flac");
                second.Report(5, "Starting");
                return Task.CompletedTask;
            }
        );

        var output = outWriter.ToString();
        Assert.Contains("[1/2]", output);
        Assert.Contains("[2/2]", output);
    }

    // ── Verbose log gate ──────────────────────────────────────────────────────

    [Fact]
    public void Log_InfoSuppressedWhenNotVerbose()
    {
        var (display, outWriter, _) = Build(verbose: false);

        display.Log(LogLevel.Info, "driver", "loading model");

        Assert.DoesNotContain("loading model", outWriter.ToString());
    }

    [Fact]
    public void Log_InfoShownWhenVerbose()
    {
        var (display, outWriter, _) = Build(verbose: true);

        display.Log(LogLevel.Info, "driver", "loading model");

        Assert.Contains("loading model", outWriter.ToString());
    }

    [Fact]
    public void Log_WarningAlwaysGoesToStandardError()
    {
        var (display, _, errWriter) = Build(verbose: false);

        display.Log(LogLevel.Warning, "driver", "something odd");

        Assert.Contains("something odd", errWriter.ToString());
    }

    // ── Activity mapping ──────────────────────────────────────────────────────

    [Fact]
    public void PhaseActivity_DownloadingMapsToHumanReadableActivity()
    {
        var activity = PhaseActivity.Describe(new JobUpdate { Phase = "downloading" });
        Assert.Equal("Downloading source", activity);
    }

    [Fact]
    public void PhaseActivity_ProgressUsesRunLabel()
    {
        var activity = PhaseActivity.Describe(
            new JobUpdate { Phase = "progress", RunLabel = "Vocals" }
        );
        Assert.Equal("Separating Vocals", activity);
    }

    [Fact]
    public void PhaseActivity_LoadingModelShowsModelCount()
    {
        var activity = PhaseActivity.Describe(
            new JobUpdate
            {
                Phase = "loading_model",
                ModelIndex = 1,
                ModelCount = 2,
            }
        );
        Assert.Equal("Loading model 1/2", activity);
    }

    [Fact]
    public void PhaseActivity_UnknownPhaseReturnsNull()
    {
        Assert.Null(PhaseActivity.Describe(new JobUpdate { Phase = "log" }));
    }

    // ── Non-interactive detection ─────────────────────────────────────────────

    [Fact]
    public void ShouldUseLiveDisplay_FalseForNonInteractiveConsole()
    {
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(new StringWriter()),
            }
        );

        Assert.False(BatchProgressFactory.ShouldUseLiveDisplay(console));
    }
}

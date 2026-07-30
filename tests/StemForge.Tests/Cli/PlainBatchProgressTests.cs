using StemForge.Cli.Progress;

namespace StemForge.Tests.Cli;

/// <summary>
/// Tests the plain (non-live) renderer's milestone-vs-percent behaviour (#51). In verbose the
/// renderer must emit one line per phase/model transition and drop the periodic percentage ticks
/// (so the streamed driver logs are not buried). In non-verbose (redirected/CI) the periodic
/// percentage line is the only progress signal and must still be printed.
/// </summary>
public sealed class PlainBatchProgressTests
{
    // Drives a single input through a sequence of (percent, activity) reports and returns the
    // standard-out lines it produced.
    private static IReadOnlyList<string> Run(
        bool verbose,
        params (int Percent, string? Activity)[] reports
    )
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var progress = new PlainBatchProgress(stdout, stderr, verbose);

        using (var input = progress.BeginInput(0, 1, "track"))
        {
            foreach (var (percent, activity) in reports)
                input.Report(percent, activity);
        }

        return stdout.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Verbose_PercentAdvancesWithoutActivityChange_EmitsNoProgressLine()
    {
        // Same activity, climbing percentage: in verbose nothing but the one-time announce prints.
        var lines = Run(
            verbose: true,
            (0, "Separating"),
            (10, "Separating"),
            (20, "Separating"),
            (50, "Separating")
        );

        // Line 0 is the one-time input announce; the first activity also prints one milestone line.
        Assert.Equal("[1/1] track", lines[0]);
        var milestoneLines = lines.Where(l => l.Contains('%')).ToList();
        Assert.Single(milestoneLines);
        Assert.Contains("Separating", milestoneLines[0]);
    }

    [Fact]
    public void Verbose_ActivityChanges_EmitsOneLinePerTransition()
    {
        var lines = Run(
            verbose: true,
            (5, "Downloading source"),
            (10, "Downloading source"),
            (30, "Loading model 1/2"),
            (60, "Separating"),
            (60, "Separating")
        );

        var milestoneLines = lines.Where(l => l.Contains('%')).ToList();
        Assert.Equal(3, milestoneLines.Count);
        Assert.Contains("Downloading source", milestoneLines[0]);
        Assert.Contains("Loading model 1/2", milestoneLines[1]);
        Assert.Contains("Separating", milestoneLines[2]);
    }

    [Fact]
    public void NonVerbose_PercentAdvancesWithoutActivityChange_StillEmitsPercentLines()
    {
        // Redirected/CI: the periodic percentage line is the only progress signal, so each visible
        // step (every 10%) must still print even though the activity text is unchanged.
        var lines = Run(
            verbose: false,
            (0, "Separating"),
            (10, "Separating"),
            (20, "Separating"),
            (50, "Separating")
        );

        var percentLines = lines.Where(l => l.Contains('%')).ToList();
        // 0, 10, 20, 50 are four distinct deca-percent buckets.
        Assert.Equal(4, percentLines.Count);
    }

    [Fact]
    public void NonVerbose_SubStepPercentChange_DoesNotEmitExtraLine()
    {
        // Two reports within the same 10% bucket and same activity print only once.
        var lines = Run(verbose: false, (11, "Separating"), (13, "Separating"));

        var percentLines = lines.Where(l => l.Contains('%')).ToList();
        Assert.Single(percentLines);
    }
}

using StemForge.Cli.Progress;

namespace StemForge.Tests.Commands;

public sealed class JobProgressReporterTests
{
    private sealed class SpyInputProgress : IInputProgress
    {
        public List<(int Percent, string? Activity)> Reports { get; } = [];

        public void Report(int overallPercent, string? activity) =>
            Reports.Add((overallPercent, activity));

        public void Complete(InputOutcome outcome, string? message) { }

        public void Dispose() { }
    }

    [Fact]
    public void Percentage_never_decreases()
    {
        var spy = new SpyInputProgress();
        var reporter = JobProgressReporter.For(spy);

        reporter.Report(
            new JobUpdate
            {
                Phase = "progress",
                RunLabel = "Vocals",
                OverallPercent = 40,
            }
        );
        // The pipeline re-emits the run's start percentage on non-progress phases.
        reporter.Report(new JobUpdate { Phase = "loading_model", OverallPercent = 0 });
        reporter.Report(
            new JobUpdate
            {
                Phase = "progress",
                RunLabel = "Vocals",
                OverallPercent = 55,
            }
        );

        Assert.Equal(new[] { 40, 40, 55 }, spy.Reports.Select(r => r.Percent));
    }

    [Fact]
    public void Remembers_model_for_later_separating_ticks()
    {
        var spy = new SpyInputProgress();
        var reporter = JobProgressReporter.For(spy);

        // The driver names the model on loading_model...
        reporter.Report(
            new JobUpdate
            {
                Phase = "loading_model",
                ModelIndex = 2,
                ModelCount = 2,
                Model = "Kim_Vocal_2",
                OverallPercent = 50,
            }
        );
        // ...but not on the separating ticks that follow.
        reporter.Report(
            new JobUpdate
            {
                Phase = "progress",
                RunLabel = "Vocals Full",
                OverallPercent = 60,
            }
        );

        var activity = spy.Reports[^1].Activity;
        Assert.NotNull(activity);
        Assert.Contains("model 2/2", activity);
        Assert.Contains("Kim_Vocal_2", activity);
    }

    [Fact]
    public void Single_model_run_shows_no_model_index()
    {
        var spy = new SpyInputProgress();
        var reporter = JobProgressReporter.For(spy);

        reporter.Report(
            new JobUpdate
            {
                Phase = "progress",
                RunLabel = "Vocals",
                ModelIndex = 1,
                ModelCount = 1,
                OverallPercent = 30,
            }
        );

        Assert.Equal("Separating Vocals", spy.Reports[^1].Activity);
    }
}

namespace StemForge.Tests.Separation;

public sealed class JobProgressTests
{
    [Theory]
    [InlineData(JobPhase.DownloadingModel, "downloading_model")]
    [InlineData(JobPhase.LoadingModel, "loading_model")]
    [InlineData(JobPhase.Separating, "separating")]
    [InlineData(JobPhase.Ensembling, "ensembling")]
    public void UpdatePhase_PhaseProgress_IsTheDriverProtocolLabel(JobPhase phase, string label) =>
        Assert.Equal(label, new PhaseProgress(phase).UpdatePhase);

    [Fact]
    public void UpdatePhase_EveryJobPhase_HasADistinctNonEmptyLabel()
    {
        var labels = Enum.GetValues<JobPhase>()
            .Select(phase => new PhaseProgress(phase).UpdatePhase);

        Assert.All(labels, label => Assert.NotEmpty(label));
        Assert.Equal(Enum.GetValues<JobPhase>().Length, labels.Distinct().Count());
    }

    [Fact]
    public void UpdatePhase_OtherSnapshots_AreProgressLogAndStemWritten()
    {
        Assert.Equal("progress", new ProgressTick().UpdatePhase);
        Assert.Equal("log", new LogLine().UpdatePhase);
        Assert.Equal("stem_written", new StemWritten().UpdatePhase);
    }

    [Fact]
    public void UpdatePhase_UndeclaredPhaseValue_IsEmptyRatherThanThrowing() =>
        Assert.Equal(string.Empty, new PhaseProgress((JobPhase)99).UpdatePhase);
}

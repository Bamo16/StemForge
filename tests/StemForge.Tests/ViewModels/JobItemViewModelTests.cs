using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class JobItemViewModelTests
{
    private static JobItemViewModel NewJob(int maxLogLines = 500) =>
        new(
            new JobRecord(Guid.NewGuid(), @"C:\music\song.flac", null, [], "out", "models"),
            maxLogLines
        );

    [Fact]
    public void InputFileName_StartsAsTheSourceFilesName() =>
        Assert.Equal("song.flac", NewJob().InputFileName);

    [Fact]
    public void AppendLog_Lines_ShowOneBelowTheOther()
    {
        var job = NewJob();

        job.AppendLog("first");
        job.AppendLog("second");

        Assert.Equal("first\nsecond", job.LogOutput);
    }

    [Fact]
    public void AppendLog_PastTheCap_DropsTheOldestLines()
    {
        // 50 is the floor: a configured cap below it is raised to it.
        var job = NewJob(maxLogLines: 10);

        for (var i = 0; i < 60; i++)
            job.AppendLog($"line{i}");

        var lines = job.LogOutput.Split('\n');
        Assert.Equal(50, lines.Length);
        Assert.Equal("line10", lines[0]);
        Assert.Equal("line59", lines[^1]);
    }

    [Fact]
    public void AppendLog_ConfiguredCapAboveTheFloor_IsHonored()
    {
        var job = NewJob(maxLogLines: 80);

        for (var i = 0; i < 100; i++)
            job.AppendLog($"line{i}");

        var lines = job.LogOutput.Split('\n');
        Assert.Equal(80, lines.Length);
        Assert.Equal("line20", lines[0]);
    }

    [Fact]
    public void AccumulateRawLog_IsInvisibleUntilTheFailureFlushShowsIt()
    {
        var job = NewJob();
        job.AppendLog("timeline");

        job.AccumulateRawLog("raw one");
        job.AccumulateRawLog("raw two");

        Assert.Equal("timeline", job.LogOutput);

        job.FlushRawLogToOutput();

        Assert.Equal("raw one\nraw two", job.LogOutput);
    }

    [Fact]
    public void FlushRawLogToOutput_NothingAccumulated_KeepsTheTimeline()
    {
        var job = NewJob();
        job.AppendLog("timeline");

        job.FlushRawLogToOutput();

        Assert.Equal("timeline", job.LogOutput);
    }

    [Fact]
    public void AccumulateRawLog_PastTheCap_KeepsTheNewestLines()
    {
        var job = NewJob();

        for (var i = 0; i < 600; i++)
            job.AccumulateRawLog($"raw{i}");
        job.FlushRawLogToOutput();

        var lines = job.LogOutput.Split('\n');
        Assert.Equal(500, lines.Length);
        Assert.Equal("raw100", lines[0]);
        Assert.Equal("raw599", lines[^1]);
    }

    [Theory]
    [InlineData(JobStatus.Queued, "QUEUED")]
    [InlineData(JobStatus.Running, "RUNNING")]
    [InlineData(JobStatus.Done, "DONE")]
    [InlineData(JobStatus.Failed, "FAILED")]
    [InlineData(JobStatus.Cancelled, "CANCELLED")]
    public void StatusLabel_IsTheUpperCaseStatus(JobStatus status, string expected) =>
        Assert.Equal(expected, new JobItemViewModel(NewJob().Job) { Status = status }.StatusLabel);

    [Theory]
    [InlineData(JobStatus.Queued, false, false)]
    [InlineData(JobStatus.Running, false, true)]
    [InlineData(JobStatus.Done, true, true)]
    [InlineData(JobStatus.Failed, true, false)]
    [InlineData(JobStatus.Cancelled, true, false)]
    public void Status_DecidesTerminalAndProgressVisibility(
        JobStatus status,
        bool terminal,
        bool showProgress
    )
    {
        var job = NewJob();

        job.Status = status;

        Assert.Equal(terminal, job.IsTerminal);
        Assert.Equal(showProgress, job.ShowProgress);
    }

    [Fact]
    public void ProgressLabel_ShowsThePresetCounterOnlyWhileRunning()
    {
        var job = NewJob();
        job.PresetCounter = "2/3";

        Assert.Equal("—", job.ProgressLabel);

        job.Status = JobStatus.Running;
        Assert.Equal("2/3", job.ProgressLabel);

        job.PresetCounter = string.Empty;
        Assert.Equal("—", job.ProgressLabel);

        job.PresetCounter = "3/3";
        job.Status = JobStatus.Done;
        Assert.Equal("—", job.ProgressLabel);
    }

    [Fact]
    public void Status_Changed_RaisesTheDependentProperties()
    {
        var job = NewJob();
        List<string?> raised = [];
        job.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        job.Status = JobStatus.Running;

        Assert.Contains(nameof(JobItemViewModel.StatusLabel), raised);
        Assert.Contains(nameof(JobItemViewModel.IsRunning), raised);
        Assert.Contains(nameof(JobItemViewModel.IsTerminal), raised);
        Assert.Contains(nameof(JobItemViewModel.ShowProgress), raised);
        Assert.Contains(nameof(JobItemViewModel.ProgressLabel), raised);
    }

    [Fact]
    public void CancelCommand_HandsTheJobToTheCancelCallback()
    {
        var job = NewJob();
        JobItemViewModel? cancelled = null;
        job.CancelRequested = requested => cancelled = requested;

        job.CancelCommand.Execute(null);

        Assert.Same(job, cancelled);
    }

    [Fact]
    public void ToggleExpandCommand_FlipsIsExpanded()
    {
        var job = NewJob();

        job.ToggleExpandCommand.Execute(null);
        Assert.True(job.IsExpanded);

        job.ToggleExpandCommand.Execute(null);
        Assert.False(job.IsExpanded);
    }
}

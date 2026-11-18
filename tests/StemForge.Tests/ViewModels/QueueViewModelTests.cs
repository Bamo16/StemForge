using Microsoft.Extensions.DependencyInjection;
using StemForge.Services;
using StemForge.Tests.TestDoubles;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class QueueViewModelTests
{
    private static JobQueueService NewQueue()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AppSettings());
        services.AddSingleton<ISeparatorDriverService>(new FakeSeparatorDriverService());
        services.AddStemForgeCore();
        var provider = services.BuildServiceProvider();
        return new JobQueueService(
            provider.GetRequiredService<SeparationPipeline>(),
            provider.GetRequiredService<AppSettings>()
        );
    }

    private static JobItemViewModel Job(JobStatus status = JobStatus.Queued) =>
        new(new JobRecord(Guid.NewGuid(), @"C:\music\song.flac", null, [], "out", "models"), 500)
        {
            Status = status,
        };

    [Fact]
    public void SummaryLabel_NoJobs_SaysNoJobs() =>
        Assert.Equal("No jobs", new QueueViewModel(NewQueue()).SummaryLabel);

    [Fact]
    public void SummaryLabel_IdleJobs_ShowsOnlyTheCount()
    {
        var queue = NewQueue();
        var vm = new QueueViewModel(queue);

        queue.Jobs.Add(Job());
        queue.Jobs.Add(Job(JobStatus.Done));

        Assert.Equal("2 jobs", vm.SummaryLabel);
    }

    [Fact]
    public void SummaryLabel_SingleJob_IsSingular()
    {
        var queue = NewQueue();
        var vm = new QueueViewModel(queue);

        queue.Jobs.Add(Job());

        Assert.Equal("1 job", vm.SummaryLabel);
    }

    [Fact]
    public void SummaryLabel_AddingARunningJob_MentionsHowManyAreRunning()
    {
        var queue = NewQueue();
        var vm = new QueueViewModel(queue);

        queue.Jobs.Add(Job());
        queue.Jobs.Add(Job(JobStatus.Running));

        Assert.Equal("2 jobs · 1 running", vm.SummaryLabel);
    }

    [Fact]
    public void SummaryLabel_JobsAlreadyThereWhenTheViewModelIsBuilt_AreCounted()
    {
        var queue = NewQueue();
        queue.Jobs.Add(Job(JobStatus.Running));

        Assert.Equal("1 job · 1 running", new QueueViewModel(queue).SummaryLabel);
    }

    [Fact]
    public void ClearDoneCommand_RemovesFinishedJobsAndKeepsTheRest()
    {
        var queue = NewQueue();
        var vm = new QueueViewModel(queue);
        var running = Job(JobStatus.Running);
        var waiting = Job();
        queue.Jobs.Add(Job(JobStatus.Done));
        queue.Jobs.Add(Job(JobStatus.Failed));
        queue.Jobs.Add(Job(JobStatus.Cancelled));
        queue.Jobs.Add(running);
        queue.Jobs.Add(waiting);

        vm.ClearDoneCommand.Execute(null);

        Assert.Equal([running, waiting], vm.Jobs);
        Assert.Equal("2 jobs · 1 running", vm.SummaryLabel);
    }

    [Fact]
    public void ClearDoneCommand_EverythingFinished_LeavesNoJobs()
    {
        var queue = NewQueue();
        var vm = new QueueViewModel(queue);
        queue.Jobs.Add(Job(JobStatus.Done));

        vm.ClearDoneCommand.Execute(null);

        Assert.Empty(vm.Jobs);
        Assert.Equal("No jobs", vm.SummaryLabel);
    }

    [Fact]
    public void Jobs_AreTheQueuesOwnCollection()
    {
        var queue = NewQueue();

        Assert.Same(queue.Jobs, new QueueViewModel(queue).Jobs);
    }
}

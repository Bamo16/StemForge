using System.Diagnostics;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Characterization tests for the driver event dispatch path. They feed representative JSON for
/// every inbound event type through the same dispatcher the read loop uses and assert the resulting
/// <see cref="JobProgress"/> / <see cref="JobResult"/> / TCS behavior. The wire format mirrors what
/// separator_driver.py emits; these tests pin the protocol so the typed-event refactor stays
/// behavior preserving.
/// </summary>
public sealed class SeparatorDriverServiceDispatchTests
{
    private static SeparatorDriverService NewService() => new(new AppPaths(new AppSettings()));

    /// <summary>Synchronous progress sink so reports are observable without a sync context.</summary>
    private sealed class ProgressCollector : IProgress<JobProgress>
    {
        private readonly List<JobProgress> _reports = [];
        public IReadOnlyList<JobProgress> Reports => _reports;
        public JobProgress Last => _reports[^1];

        public void Report(JobProgress value) => _reports.Add(value);
    }

    // ── ready ────────────────────────────────────────────────────────────────

    [Fact]
    public void Ready_CompletesReadyHandshake()
    {
        var svc = NewService();
        var ready = svc.ArmReadyForTest();

        svc.DispatchLineForTest(
            """{"event":"ready","driver_version":"1.0","separator_version":"0.40","device":"cuda"}"""
        );

        Assert.True(ready.IsCompletedSuccessfully);
    }

    // ── phase ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Phase_DownloadingModel_ReportsModelFields()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """
            {"event":"phase","id":"job_1","phase":"downloading_model","model":"foo.ckpt","model_index":1,"model_count":3,"cached":false}
            """
        );

        var report = Assert.IsType<PhaseProgress>(progress.Last);
        Assert.Equal(JobPhase.DownloadingModel, report.Phase);
        Assert.Equal("foo.ckpt", report.Model);
        Assert.Equal(1, report.ModelIndex);
        Assert.Equal(3, report.ModelCount);
        Assert.False(report.Cached);
    }

    [Fact]
    public void Phase_LoadingModel_ReportsModelFieldsWithoutCached()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """
            {"event":"phase","id":"job_1","phase":"loading_model","model":"foo.ckpt","model_index":2,"model_count":3,"cached":true}
            """
        );

        var report = Assert.IsType<PhaseProgress>(progress.Last);
        Assert.Equal(JobPhase.LoadingModel, report.Phase);
        Assert.Equal("foo.ckpt", report.Model);
        Assert.Equal(2, report.ModelIndex);
        Assert.Equal(3, report.ModelCount);
        // loading_model deliberately omits the cached flag.
        Assert.Null(report.Cached);
    }

    [Fact]
    public void Phase_Ensembling_ReportsStem()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """{"event":"phase","id":"job_1","phase":"ensembling","stem":"Vocals"}"""
        );

        var report = Assert.IsType<PhaseProgress>(progress.Last);
        Assert.Equal(JobPhase.Ensembling, report.Phase);
        Assert.Equal("Vocals", report.Stem);
    }

    [Fact]
    public void Phase_Separating_ReportsModelCount()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """{"event":"phase","id":"job_1","phase":"separating","model_count":2}"""
        );

        var report = Assert.IsType<PhaseProgress>(progress.Last);
        Assert.Equal(JobPhase.Separating, report.Phase);
        Assert.Equal(2, report.ModelCount);
    }

    [Fact]
    public void Phase_WithoutActiveJob_IsIgnored()
    {
        var svc = NewService();

        // No active job installed; dispatch must not throw.
        svc.DispatchLineForTest(
            """{"event":"phase","id":"job_1","phase":"separating","model_count":2}"""
        );
    }

    // ── progress ───────────────────────────────────────────────────────────────

    [Fact]
    public void Progress_ReportsCurrentTotalFinal()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """{"event":"progress","id":"job_1","phase":"separate","current":1234,"total":5678,"final":true}"""
        );

        var report = Assert.IsType<ProgressTick>(progress.Last);
        Assert.Equal(1234, report.Current);
        Assert.Equal(5678, report.Total);
        Assert.True(report.Final);
    }

    [Fact]
    public void Progress_MissingTotalAndFinal_LeavesThemNull()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """{"event":"progress","id":"job_1","phase":"download","current":42}"""
        );

        var report = Assert.IsType<ProgressTick>(progress.Last);
        Assert.Equal(42, report.Current);
        Assert.Null(report.Total);
        Assert.Null(report.Final);
    }

    // ── log ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Log_ReportsLevelAndMessageAndAppendsActivityTail()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """{"event":"log","id":"job_1","level":"warning","message":"Detected input bit depth: 24-bit"}"""
        );

        var report = Assert.IsType<LogLine>(progress.Last);
        Assert.Equal("warning", report.Level);
        Assert.Equal("Detected input bit depth: 24-bit", report.Message);
        Assert.Contains("Detected input bit depth: 24-bit", svc.ActivityTailForTest());
    }

    [Fact]
    public void Log_DefaultsMissingLevelToInfo()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest("""{"event":"log","id":"job_1","message":"hello"}""");

        Assert.Equal("info", Assert.IsType<LogLine>(progress.Last).Level);
    }

    // ── stem_written ───────────────────────────────────────────────────────────

    [Fact]
    public void StemWritten_ReportsStemAndPath()
    {
        var (svc, progress) = NewServiceWithJob();

        svc.DispatchLineForTest(
            """{"event":"stem_written","id":"job_1","stem":"Vocals","path":"/out/song_vocals.flac"}"""
        );

        var report = Assert.IsType<StemWritten>(progress.Last);
        Assert.Equal("Vocals", report.Stem);
        Assert.Equal("/out/song_vocals.flac", report.Path);
    }

    // ── job_completed ──────────────────────────────────────────────────────────

    [Fact]
    public async Task JobCompleted_ResolvesResultWithOutputs()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        svc.DispatchLineForTest(
            """
            {"event":"job_completed","id":"job_1","outputs":[{"stem":"Vocals","path":"/out/v.flac"}],"discarded":[{"stem":"Instrumental","path":"/out/i.flac"}],"duration_seconds":12.5}
            """
        );

        var jobResult = await result.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        Assert.True(jobResult.Succeeded);
        Assert.Equal(12.5, jobResult.DurationSeconds);
        var output = Assert.Single(jobResult.Outputs);
        Assert.Equal("Vocals", output.Stem);
        Assert.Equal("/out/v.flac", output.Path);
        var discarded = Assert.Single(jobResult.Discarded);
        Assert.Equal("Instrumental", discarded.Stem);
    }

    [Fact]
    public async Task JobCompleted_DropsOutputsWithEmptyPath()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        svc.DispatchLineForTest(
            """
            {"event":"job_completed","id":"job_1","outputs":[{"stem":"Vocals","path":""},{"stem":"Drums","path":"/out/d.flac"}],"discarded":[],"duration_seconds":1.0}
            """
        );

        var jobResult = await result.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        var output = Assert.Single(jobResult.Outputs);
        Assert.Equal("Drums", output.Stem);
    }

    // ── job_cancelled ──────────────────────────────────────────────────────────

    [Fact]
    public async Task JobCancelled_AcknowledgesAndCancelsResult()
    {
        var (svc, _, result, cancelAck) = NewServiceWithJobResult();

        svc.DispatchLineForTest("""{"event":"job_cancelled","id":"job_1"}""");

        await cancelAck.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            result.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)
        );
    }

    // ── job_failed ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task JobFailed_ResolvesFailureResultWithErrorAndTraceback()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        svc.DispatchLineForTest(
            """{"event":"job_failed","id":"job_1","error":"CUDA out of memory","traceback":"Traceback...\nOOM"}"""
        );

        var jobResult = await result.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        Assert.False(jobResult.Succeeded);
        Assert.Equal("CUDA out of memory", jobResult.ErrorMessage);
        Assert.Equal("Traceback...\nOOM", jobResult.Traceback);
        Assert.Empty(jobResult.Outputs);
    }

    [Fact]
    public async Task JobFailed_MissingError_DefaultsToUnknown()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        svc.DispatchLineForTest("""{"event":"job_failed","id":"job_1"}""");

        var jobResult = await result.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("unknown", jobResult.ErrorMessage);
        Assert.Null(jobResult.Traceback);
    }

    // ── error / bye / unknown / malformed ──────────────────────────────────────

    [Fact]
    public void Error_DoesNotCompleteJob()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        svc.DispatchLineForTest("""{"event":"error","error":"a job is already running"}""");

        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void Bye_IsIgnored()
    {
        var svc = NewService();
        svc.DispatchLineForTest("""{"event":"bye"}""");
    }

    [Fact]
    public void TransportNoise_IsTolerated()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        // Stray non-JSON output from a native dependency (torch/onnxruntime/a print) is not a
        // protocol message; it is tolerated and the stream keeps reading.
        svc.DispatchLineForTest("this is not json");
        svc.DispatchLineForTest("Some weights were not initialized from the checkpoint");

        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void ContractViolation_DoesNotCompleteJobOrTearDownStream()
    {
        var (svc, _, result, _) = NewServiceWithJobResult();

        // Now that the vocabulary is closed and co-versioned, a JSON line with an unknown event,
        // an unknown phase, or no discriminator is a contract violation: it fails loud (the Debug
        // assertion is suppressed here so the test is deterministic) but must not complete the job
        // or tear down the reader, so a real run degrades gracefully in release.
        WithAssertionsSuppressed(() =>
        {
            svc.DispatchLineForTest("""{"event":"speculative_future_event","id":"job_1"}""");
            svc.DispatchLineForTest("""{"event":"phase","id":"job_1","phase":"writing_output"}""");
            svc.DispatchLineForTest("""{"id":"job_1","current":1}""");
        });

        Assert.False(result.IsCompleted);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="action"/> with Debug/Trace assertion listeners removed, so a
    /// fail-loud <c>Debug.Fail</c> on the dispatch path does not abort the test or pop UI.</summary>
    private static void WithAssertionsSuppressed(Action action)
    {
        var saved = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(saved, 0);
        Trace.Listeners.Clear();
        try
        {
            action();
        }
        finally
        {
            Trace.Listeners.AddRange(saved);
        }
    }

    private static (SeparatorDriverService Svc, ProgressCollector Progress) NewServiceWithJob()
    {
        var svc = NewService();
        var progress = new ProgressCollector();
        svc.BeginJobForTest("job_1", progress);
        return (svc, progress);
    }

    private static (
        SeparatorDriverService Svc,
        ProgressCollector Progress,
        Task<JobResult> Result,
        Task CancelAcknowledged
    ) NewServiceWithJobResult()
    {
        var svc = NewService();
        var progress = new ProgressCollector();
        var (result, cancelAck) = svc.BeginJobForTest("job_1", progress);
        return (svc, progress, result, cancelAck);
    }
}

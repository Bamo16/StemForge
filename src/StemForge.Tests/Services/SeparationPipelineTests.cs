using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SeparationPipeline"/>. Uses a temporary directory for
/// real file I/O (tagging) and <see cref="SpySeparatorDriverService"/> for driver calls.
/// </summary>
public sealed class SeparationPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SpySeparatorDriverService _driver;
    private readonly AppSettings _settings;
    private readonly SeparationPipeline _pipeline;

    // Minimal valid FLAC: magic + last-metadata STREAMINFO block (all-zero stream info).
    // TagLibSharp can open and Save() this file without errors.
    private static readonly byte[] _minimalFlac =
    [
        // fLaC marker
        0x66,
        0x4C,
        0x61,
        0x43,
        // STREAMINFO block: last-metadata-block=1 (bit7), type=0 (bits 6-0), length=34 (3 bytes)
        0x80,
        0x00,
        0x00,
        0x22,
        // STREAMINFO payload (34 bytes, all zero = "unknown")
        .. new byte[34],
    ];

    public SeparationPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _driver = new SpySeparatorDriverService();
        _settings = new AppSettings();
        var paths = new AppPaths(_settings);

        // YouTubeAudioService and IThumbnailFetcher are not exercised in local-file tests;
        // pass null — the pipeline only reaches those paths for URL-sourced jobs.
        _pipeline = new SeparationPipeline(
            _driver,
            youTubeAudio: null!,
            thumbnailFetcher: null!,
            runner: new FakeProcessRunner(),
            _settings,
            paths,
            AppInfo.Current
        );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string CreateFlacFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, _minimalFlac);
        return path;
    }

    private JobRecord MakeJob(
        string inputFile,
        IReadOnlyList<Preset> presets,
        bool extractDrums = false
    ) =>
        new(
            Guid.NewGuid(),
            InputFilePath: inputFile,
            SourceUrl: null,
            Presets: presets,
            OutputDir: _tempDir,
            ModelsDir: _tempDir,
            StemOutputFormat: AudioFormat.Flac,
            ExtractDrums: extractDrums
        );

    private static Preset MakeSingleModelPreset(string id, string label) =>
        new(
            Id: id,
            Label: label,
            Category: PresetCategory.Vocals,
            Description: "Test preset",
            ModelCount: 1,
            Vram: "",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "test_model.ckpt"
        );

    private static JobResult SuccessResult(params string[] outputPaths) =>
        new(
            Succeeded: true,
            Outputs: [.. outputPaths.Select(p => new JobOutput(Stem: "Vocals", Path: p))],
            Discarded: [],
            DurationSeconds: 1.0,
            ErrorMessage: null,
            Traceback: null
        );

    private static JobResult FailureResult(string errorMessage = "Separation failed") =>
        new(
            Succeeded: false,
            Outputs: [],
            Discarded: [],
            DurationSeconds: 0.0,
            ErrorMessage: errorMessage,
            Traceback: null
        );

    // ── Test 1: Sequence of runs ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TwoPresets_CallsDriverTwice()
    {
        var input = CreateFlacFile("input.flac");
        var out1 = CreateFlacFile("stem1.flac");
        var out2 = CreateFlacFile("stem2.flac");

        var preset1 = MakeSingleModelPreset("p1", "Preset One");
        var preset2 = MakeSingleModelPreset("p2", "Preset Two");
        var job = MakeJob(input, [preset1, preset2]);

        _driver.EnqueueRun(SuccessResult(out1));
        _driver.EnqueueRun(SuccessResult(out2));

        var outputs = await _pipeline.RunAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(2, _driver.CallCount);
        Assert.Equal(2, outputs.Count);
        Assert.Contains(out1, outputs);
        Assert.Contains(out2, outputs);
    }

    [Fact]
    public async Task RunAsync_TwoPresets_BothRequestsUseCorrectInputPath()
    {
        var input = CreateFlacFile("input.flac");
        var out1 = CreateFlacFile("stem1.flac");
        var out2 = CreateFlacFile("stem2.flac");

        var preset1 = MakeSingleModelPreset("p1", "Preset One");
        var preset2 = MakeSingleModelPreset("p2", "Preset Two");
        var job = MakeJob(input, [preset1, preset2]);

        _driver.EnqueueRun(SuccessResult(out1));
        _driver.EnqueueRun(SuccessResult(out2));

        await _pipeline.RunAsync(job, progress: null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(input, _driver.ReceivedRequests[0].AudioPath);
        Assert.Equal(input, _driver.ReceivedRequests[1].AudioPath);
    }

    // ── Test 2: Tagging per output ────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SinglePreset_TagLibModifiesOutputFile()
    {
        var input = CreateFlacFile("input.flac");
        var outputPath = CreateFlacFile("stem.flac");

        // Record write time before the pipeline runs.
        var beforeWrite = new FileInfo(outputPath).LastWriteTimeUtc;

        // Brief pause so any subsequent write has a different timestamp.
        await Task.Delay(10, TestContext.Current.CancellationToken);

        var preset = MakeSingleModelPreset("p1", "Vocal Clean");
        var job = MakeJob(input, [preset]);

        _driver.EnqueueRun(SuccessResult(outputPath));

        await _pipeline.RunAsync(job, progress: null, ct: TestContext.Current.CancellationToken);

        var afterWrite = new FileInfo(outputPath).LastWriteTimeUtc;
        Assert.True(
            afterWrite > beforeWrite,
            "TagLibSharp should have rewritten the output file when ApplyToFile was called."
        );
    }

    // ── Test 3: Progress percentage math ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_ProgressOnSecondRun_OverallPercentIs75()
    {
        // Equal-weight case: 2 presets each with ModelCount=1, totalModelWeight=2.
        // Second run (runIndex=1, runStartWeight=1) emits progress at 50/100 with 1 model.
        // withinModel = min(100, 50*100/100) = 50
        // withinPreset = ((1-1)*100 + 50) / 1 = 50
        // overall = min(99, round((1 + 50*1/100) * 100 / 2)) = min(99, round(75)) = 75
        var input = CreateFlacFile("input.flac");
        var out1 = CreateFlacFile("run1.flac");
        var out2 = CreateFlacFile("run2.flac");

        var preset1 = MakeSingleModelPreset("p1", "Preset One");
        var preset2 = MakeSingleModelPreset("p2", "Preset Two");
        var job = MakeJob(input, [preset1, preset2]);

        // First run: no progress events.
        _driver.EnqueueRun(SuccessResult(out1));

        // Second run: loading_model then progress at 50/100.
        _driver.EnqueueRun(
            SuccessResult(out2),
            [
                new JobProgress
                {
                    Kind = JobProgressKind.Phase,
                    Phase = JobPhase.LoadingModel,
                    ModelIndex = 1,
                    ModelCount = 1,
                },
                new JobProgress
                {
                    Kind = JobProgressKind.Progress,
                    Current = 50,
                    Total = 100,
                },
            ]
        );

        var updates = new System.Collections.Concurrent.ConcurrentBag<JobUpdate>();
        var progressCollector = new Progress<JobUpdate>(updates.Add);

        await _pipeline.RunAsync(job, progressCollector, ct: TestContext.Current.CancellationToken);

        // Progress<T> posts callbacks to the captured SynchronizationContext (xUnit sets one),
        // so callbacks arrive asynchronously via the ThreadPool. Poll until the expected update
        // appears rather than sleeping a fixed amount.
        for (var i = 0; i < 100 && !updates.Any(u => u is { Phase: "progress", RunIndex: 1 }); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        var progressUpdate = updates.LastOrDefault(u => u is { Phase: "progress", RunIndex: 1 });

        Assert.NotNull(progressUpdate);
        Assert.Equal(75, progressUpdate!.OverallPercent);
    }

    [Fact]
    public async Task RunAsync_ModelCountWeighting_ProgressReflectsWeight()
    {
        // Preset A has ModelCount=3, Preset B has ModelCount=1. totalModelWeight=4.
        // Preset A completes (run_complete): runStartWeight=0, weight=3 → 3*100/4 = 75%.
        // Preset B at progress 50/100: runStartWeight=3, weight=1.
        // withinPreset=50, overall = min(99, round((3 + 50*1/100)*100/4)) = min(99, round(87.5)) = 88.
        var input = CreateFlacFile("input.flac");
        var outA = CreateFlacFile("stemA.flac");
        var outB = CreateFlacFile("stemB.flac");

        var presetA = new Preset(
            Id: "pA",
            Label: "Heavy",
            Category: PresetCategory.Vocals,
            Description: "3-model preset",
            ModelCount: 3,
            Vram: "",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "model_a.ckpt"
        );
        var presetB = MakeSingleModelPreset("pB", "Light");
        var job = MakeJob(input, [presetA, presetB]);

        _driver.EnqueueRun(SuccessResult(outA));
        _driver.EnqueueRun(
            SuccessResult(outB),
            [
                new JobProgress
                {
                    Kind = JobProgressKind.Phase,
                    Phase = JobPhase.LoadingModel,
                    ModelIndex = 1,
                    ModelCount = 1,
                },
                new JobProgress
                {
                    Kind = JobProgressKind.Progress,
                    Current = 50,
                    Total = 100,
                },
            ]
        );

        var updates = new System.Collections.Concurrent.ConcurrentBag<JobUpdate>();
        await _pipeline.RunAsync(
            job,
            new Progress<JobUpdate>(updates.Add),
            ct: TestContext.Current.CancellationToken
        );

        for (var i = 0; i < 100 && !updates.Any(u => u is { Phase: "progress", RunIndex: 1 }); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        var runAComplete = updates.LastOrDefault(u => u is { Phase: "run_complete", RunIndex: 0 });
        var runBProgress = updates.LastOrDefault(u => u is { Phase: "progress", RunIndex: 1 });

        Assert.NotNull(runAComplete);
        Assert.Equal(75, runAComplete!.OverallPercent);

        Assert.NotNull(runBProgress);
        Assert.Equal(88, runBProgress!.OverallPercent);
    }

    // ── Test 4: First-preset failure stops pipeline ───────────────────────────

    [Fact]
    public async Task RunAsync_FirstPresetFails_ThrowsInvalidOperationException()
    {
        var input = CreateFlacFile("input.flac");
        var preset1 = MakeSingleModelPreset("p1", "Preset One");
        var preset2 = MakeSingleModelPreset("p2", "Preset Two");
        var job = MakeJob(input, [preset1, preset2]);

        _driver.EnqueueRun(FailureResult("Preset 1 failed"));
        // Do NOT enqueue a second result: if the pipeline wrongly calls driver twice it throws.

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _pipeline.RunAsync(job, progress: null, ct: TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task RunAsync_FirstPresetFails_DriverCalledExactlyOnce()
    {
        var input = CreateFlacFile("input.flac");
        var preset1 = MakeSingleModelPreset("p1", "Preset One");
        var preset2 = MakeSingleModelPreset("p2", "Preset Two");
        var job = MakeJob(input, [preset1, preset2]);

        _driver.EnqueueRun(FailureResult("Preset 1 failed"));

        try
        {
            await _pipeline.RunAsync(
                job,
                progress: null,
                ct: TestContext.Current.CancellationToken
            );
        }
        catch (InvalidOperationException) { }

        Assert.Equal(1, _driver.CallCount);
    }
}

using StemForge.Core.Models;
using StemForge.Core.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SeparationPipeline"/>. Uses a temporary directory for
/// real file I/O (tagging) and <see cref="MockSeparatorDriverService"/> for driver calls.
/// </summary>
public sealed class SeparationPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockSeparatorDriverService _driver;
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
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
    ];

    public SeparationPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _driver = new MockSeparatorDriverService();
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
        new JobRecord(
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
        new Preset(
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
        new JobResult(
            Succeeded: true,
            Outputs: outputPaths.Select(p => new JobOutput(Stem: "Vocals", Path: p)).ToList(),
            Discarded: [],
            DurationSeconds: 1.0,
            ErrorMessage: null,
            Traceback: null
        );

    private static JobResult FailureResult(string errorMessage = "Separation failed") =>
        new JobResult(
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

        var outputs = await _pipeline.RunAsync(job, progress: null, ct: default);

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

        await _pipeline.RunAsync(job, progress: null, ct: default);

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
        await Task.Delay(10);

        var preset = MakeSingleModelPreset("p1", "Vocal Clean");
        var job = MakeJob(input, [preset]);

        _driver.EnqueueRun(SuccessResult(outputPath));

        await _pipeline.RunAsync(job, progress: null, ct: default);

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
        // 2 preset runs. Second run (runIndex=1) emits progress at 50/100 with 1 model.
        // withinModel = min(100, 50*100/100) = 50
        // withinPreset = ((1-1)*100 + 50) / 1 = 50
        // overall = round((1*100 + 50) / 2) = round(75) = 75
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
                    Phase = "loading_model",
                    ModelIndex = 1,
                    ModelCount = 1,
                },
                new JobProgress
                {
                    Phase = "progress",
                    Current = 50,
                    Total = 100,
                },
            ]
        );

        var updates = new List<JobUpdate>();
        // Collect updates synchronously on the calling thread (no SynchronizationContext).
        var progressCollector = new Progress<JobUpdate>(updates.Add);

        await _pipeline.RunAsync(job, progressCollector, ct: default);

        // Progress<T> with no SynchronizationContext invokes the callback synchronously in .NET 9+;
        // allow a brief yield in case it is deferred.
        await Task.Delay(30);

        var progressUpdate = updates.LastOrDefault(u => u.Phase == "progress" && u.RunIndex == 1);

        Assert.NotNull(progressUpdate);
        Assert.Equal(75, progressUpdate!.OverallPercent);
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
            _pipeline.RunAsync(job, progress: null, ct: default)
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
            await _pipeline.RunAsync(job, progress: null, ct: default);
        }
        catch (InvalidOperationException) { }

        Assert.Equal(1, _driver.CallCount);
    }
}

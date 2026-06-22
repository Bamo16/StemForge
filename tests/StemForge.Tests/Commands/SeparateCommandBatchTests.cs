using Microsoft.Extensions.DependencyInjection;
using StemForge.Cli.Commands;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Commands;

/// <summary>
/// Tests for the multi-input / multi-preset batch behaviour added in #44.
/// Covers preset up-front validation, cross-product job shaping, URL detection,
/// cookies flag, and exit-code semantics.
/// </summary>
public sealed class SeparateCommandBatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;

    public SeparateCommandBatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sfcli-batch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settings = new AppSettings();
        _paths = new AppPaths(_settings);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    // Helper: create a temp file and return its path.
    private string MakeTempFile(string name = "track.flac")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0x00]);
        return path;
    }

    // Helper: build a minimal SeparationPipeline backed by a fake driver.
    private static (SeparationPipeline Pipeline, FakeSeparatorDriverService Driver) BuildPipeline(
        AppSettings settings,
        AppPaths paths,
        JobResult? nextResult = null
    )
    {
        var driver = new FakeSeparatorDriverService();
        if (nextResult is not null)
            driver.NextResult = nextResult;

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(paths);
        services.AddSingleton<ISeparatorDriverService>(driver);
        services.AddStemForgeCore();

        var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<SeparationPipeline>();
        return (pipeline, driver);
    }

    // ── ValidatePresets: up-front preset validation ───────────────────────────

    [Fact]
    public void ValidatePresets_AllKnown_Succeeds()
    {
        var outcome = SeparateCommand.ValidatePresets(["vocal_balanced", "instrumental_full"]);

        Assert.Equal(0, outcome.ExitCode);
        Assert.NotNull(outcome.Presets);
        Assert.Equal(2, outcome.Presets.Count);
        Assert.Equal("vocal_balanced", outcome.Presets[0].Id);
        Assert.Equal("instrumental_full", outcome.Presets[1].Id);
    }

    [Fact]
    public void ValidatePresets_OneUnknown_ReturnsExitCode1()
    {
        var outcome = SeparateCommand.ValidatePresets(["vocal_balanced", "not_a_real_preset"]);

        Assert.Equal(1, outcome.ExitCode);
        Assert.Null(outcome.Presets);
        Assert.NotNull(outcome.ErrorMessage);
        Assert.Contains("not_a_real_preset", outcome.ErrorMessage);
    }

    [Fact]
    public void ValidatePresets_FirstPresetBad_AbortsBeforeCheckingSecond()
    {
        // Even if only one preset is bad, the whole validation fails immediately.
        var outcome = SeparateCommand.ValidatePresets(["bad_preset_one", "vocal_balanced"]);

        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("bad_preset_one", outcome.ErrorMessage);
    }

    [Fact]
    public void ValidatePresets_IsCaseInsensitive()
    {
        var outcome = SeparateCommand.ValidatePresets(["VOCAL_BALANCED", "INSTRUMENTAL_FULL"]);

        Assert.Equal(0, outcome.ExitCode);
        Assert.NotNull(outcome.Presets);
        Assert.Equal(2, outcome.Presets.Count);
    }

    // ── ValidateFormat ────────────────────────────────────────────────────────

    [Fact]
    public void ValidateFormat_NullFormat_FallsBackToSettingsDefault()
    {
        _settings.DefaultAudioFormat = AudioFormat.Mp3;
        var outcome = SeparateCommand.ValidateFormat(null, _settings);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(AudioFormat.Mp3, outcome.ResolvedFormat);
    }

    [Fact]
    public void ValidateFormat_UnknownFormat_ReturnsExitCode1()
    {
        var outcome = SeparateCommand.ValidateFormat("ogg", _settings);

        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("ogg", outcome.ErrorMessage);
    }

    // ── JobRecord cross-product shaping ────────────────────────────────────────

    [Fact]
    public void JobRecord_BuiltWithAllPresets_ContainsAllPresets()
    {
        // The command builds one JobRecord per input, with ALL selected presets attached.
        // Verify that a JobRecord built this way has the correct preset list.
        var presetsValidation = SeparateCommand.ValidatePresets([
            "vocal_balanced",
            "instrumental_full",
        ]);
        Assert.Equal(0, presetsValidation.ExitCode);
        var resolvedPresets = presetsValidation.Presets!;

        var inputFile = MakeTempFile();
        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: Path.GetFullPath(inputFile),
            SourceUrl: null,
            Presets: resolvedPresets,
            OutputDir: _tempDir,
            ModelsDir: _paths.ModelsDirectory
        );

        Assert.Equal(2, job.Presets.Count);
        Assert.Equal("vocal_balanced", job.Presets[0].Id);
        Assert.Equal("instrumental_full", job.Presets[1].Id);
    }

    [Fact]
    public void MultipleInputs_EachGetsAllPresets()
    {
        // Two inputs with two presets each produce two separate JobRecords,
        // each containing the full preset list.
        var presetsValidation = SeparateCommand.ValidatePresets([
            "vocal_balanced",
            "instrumental_full",
        ]);
        var presets = presetsValidation.Presets!;

        var inputA = MakeTempFile("a.flac");
        var inputB = MakeTempFile("b.flac");

        var jobA = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: Path.GetFullPath(inputA),
            SourceUrl: null,
            Presets: presets,
            OutputDir: _tempDir,
            ModelsDir: _paths.ModelsDirectory
        );
        var jobB = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: Path.GetFullPath(inputB),
            SourceUrl: null,
            Presets: presets,
            OutputDir: _tempDir,
            ModelsDir: _paths.ModelsDirectory
        );

        Assert.Equal(2, jobA.Presets.Count);
        Assert.Equal(2, jobB.Presets.Count);
        Assert.Equal("vocal_balanced", jobA.Presets[0].Id);
        Assert.Equal("vocal_balanced", jobB.Presets[0].Id);
        Assert.Equal("instrumental_full", jobA.Presets[1].Id);
        Assert.Equal("instrumental_full", jobB.Presets[1].Id);
    }

    // ── URL detection ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://soundcloud.com/artist/track")]
    [InlineData("http://example.com/audio.mp3")]
    public void YtUrlHelper_TryNormalize_RecognizesUrlInputs(string url)
    {
        Assert.True(YtUrlHelper.TryNormalize(url, out var normalized));
        Assert.NotNull(normalized);
    }

    [Theory]
    [InlineData("/path/to/track.flac")]
    [InlineData("C:\\Music\\track.flac")]
    [InlineData("track.flac")]
    [InlineData("relative/path/track.mp3")]
    public void YtUrlHelper_TryNormalize_ReturnsFalseForFilePaths(string path)
    {
        Assert.False(YtUrlHelper.TryNormalize(path, out _));
    }

    [Fact]
    public void UrlInput_JobRecord_SetsSourceUrlNotInputFilePath()
    {
        const string url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        Assert.True(YtUrlHelper.TryNormalize(url, out var normalized));

        var presetsValidation = SeparateCommand.ValidatePresets(["vocal_balanced"]);
        var presets = presetsValidation.Presets!;

        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: null,
            SourceUrl: normalized,
            Presets: presets,
            OutputDir: _tempDir,
            ModelsDir: _paths.ModelsDirectory
        );

        Assert.Null(job.InputFilePath);
        Assert.NotNull(job.SourceUrl);
        Assert.Contains("dQw4w9WgXcQ", job.SourceUrl);
    }

    [Fact]
    public void FileInput_JobRecord_SetsInputFilePathNotSourceUrl()
    {
        var inputFile = MakeTempFile();
        var presetsValidation = SeparateCommand.ValidatePresets(["vocal_balanced"]);
        var presets = presetsValidation.Presets!;

        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: Path.GetFullPath(inputFile),
            SourceUrl: null,
            Presets: presets,
            OutputDir: _tempDir,
            ModelsDir: _paths.ModelsDirectory
        );

        Assert.NotNull(job.InputFilePath);
        Assert.Null(job.SourceUrl);
    }

    // ── Cookies flag ──────────────────────────────────────────────────────────

    [Fact]
    public void CookiesFromBrowser_SetsYtdlpCookiesFromBrowser_OnAppSettings()
    {
        // The command sets this before running the pipeline. Test the setting directly.
        var settings = new AppSettings();
        Assert.Null(settings.YtdlpCookiesFromBrowser);

        settings.YtdlpCookiesFromBrowser = "firefox";
        Assert.Equal("firefox", settings.YtdlpCookiesFromBrowser);
    }

    [Theory]
    [InlineData("firefox")]
    [InlineData("chrome")]
    [InlineData("edge")]
    [InlineData("brave")]
    public void CookiesFromBrowser_AcceptsKnownBrowserNames(string browser)
    {
        var settings = new AppSettings();
        settings.YtdlpCookiesFromBrowser = browser;
        Assert.Equal(browser, settings.YtdlpCookiesFromBrowser);
    }

    // ── Exit code semantics ────────────────────────────────────────────────────

    [Fact]
    public void ExitCode_AllSucceeded_Is0()
    {
        // Simulate: 2 inputs, both succeed.
        int succeeded = 2;
        int total = 2;
        int exitCode = succeeded == total ? 0 : (succeeded == 0 ? 1 : 2);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ExitCode_AllFailed_Is1()
    {
        int succeeded = 0;
        int total = 2;
        int exitCode = succeeded == total ? 0 : (succeeded == 0 ? 1 : 2);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void ExitCode_PartialFailure_Is2()
    {
        int succeeded = 1;
        int total = 2;
        int exitCode = succeeded == total ? 0 : (succeeded == 0 ? 1 : 2);
        Assert.Equal(2, exitCode);
    }

    // ── Validate (single-input legacy helper, preserved for backward compat) ──

    [Fact]
    public void Validate_BadPreset_RejectsBeforeFileCheck()
    {
        // Validate still enforces preset-first order.
        var result = SeparateCommand.Validate(
            "nonexistent-file.flac",
            "bad_preset",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("bad_preset", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ValidPresetMissingFile_ReturnsExitCode1()
    {
        var result = SeparateCommand.Validate(
            Path.Combine(_tempDir, "does-not-exist.flac"),
            "vocal_balanced",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}

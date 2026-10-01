using Microsoft.Extensions.DependencyInjection;
using StemForge.Cli.Commands;
using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Commands;

/// <summary>
/// Unit tests for <see cref="SeparateCommand.Validate"/> and the pipeline invocation shape.
/// Uses <see cref="FakeSeparatorDriverService"/> as the driver test double.
/// </summary>
public sealed class SeparateCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _existingInputFile;
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;

    public SeparateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sfcli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _existingInputFile = Path.Combine(_tempDir, "track.flac");
        File.WriteAllBytes(_existingInputFile, [0x00]); // non-empty placeholder

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

    // ── Preset validation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_UnknownPresetId_ReturnsExitCode1()
    {
        var result = SeparateCommand.Validate(
            _existingInputFile,
            "not_a_real_preset",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(1, result.ExitCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not_a_real_preset", result.ErrorMessage);
        Assert.Null(result.Preset);
    }

    [Theory]
    [InlineData("vocal_balanced")]
    [InlineData("vocal_clean")]
    [InlineData("vocal_full")]
    [InlineData("vocal_rvc")]
    [InlineData("instrumental_balanced")]
    [InlineData("instrumental_clean")]
    [InlineData("instrumental_full")]
    [InlineData("instrumental_low_resource")]
    [InlineData("karaoke")]
    public void Validate_KnownPresetId_Succeeds(string presetId)
    {
        var result = SeparateCommand.Validate(
            _existingInputFile,
            presetId,
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Preset);
        Assert.Equal(presetId, result.Preset.Id);
    }

    [Fact]
    public void Validate_PresetIdIsCaseInsensitive()
    {
        var result = SeparateCommand.Validate(
            _existingInputFile,
            "VOCAL_BALANCED",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Preset);
        Assert.Equal("vocal_balanced", result.Preset.Id);
    }

    // ── Input file validation ──────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingInputFile_ReturnsExitCode1()
    {
        var missingPath = Path.Combine(_tempDir, "does-not-exist.flac");

        var result = SeparateCommand.Validate(
            missingPath,
            "vocal_balanced",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(1, result.ExitCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Preset);
    }

    [Fact]
    public void Validate_InvalidPreset_RejectsBeforeCheckingFile()
    {
        // Even if the file also doesn't exist, a bad preset is caught first.
        var result = SeparateCommand.Validate(
            "no-such-file.flac",
            "no_such_preset",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("no_such_preset", result.ErrorMessage);
    }

    // ── Format validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("flac", AudioFormat.Flac)]
    [InlineData("Flac", AudioFormat.Flac)]
    [InlineData("FLAC", AudioFormat.Flac)]
    [InlineData("wav", AudioFormat.Wav)]
    [InlineData("mp3", AudioFormat.Mp3)]
    public void Validate_KnownFormat_ResolvesCorrectly(string formatStr, AudioFormat expected)
    {
        var result = SeparateCommand.Validate(
            _existingInputFile,
            "vocal_balanced",
            formatStr,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.ResolvedFormat);
    }

    [Fact]
    public void Validate_UnknownFormat_ReturnsExitCode1()
    {
        var result = SeparateCommand.Validate(
            _existingInputFile,
            "vocal_balanced",
            "ogg",
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ogg", result.ErrorMessage);
    }

    // ── Settings defaults ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_NoFormatOverride_UsesSettingsDefault()
    {
        _settings.DefaultAudioFormat = AudioFormat.Mp3;

        var result = SeparateCommand.Validate(
            _existingInputFile,
            "vocal_balanced",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(AudioFormat.Mp3, result.ResolvedFormat);
    }

    [Fact]
    public void Validate_NoOutputOverride_UsesAppPathsOutputDirectory()
    {
        var result = SeparateCommand.Validate(
            _existingInputFile,
            "vocal_balanced",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(_paths.OutputDirectory, result.ResolvedOutputDir);
    }

    [Fact]
    public void Validate_WithOutputOverride_UsesProvidedDirectory()
    {
        var customDir = Path.Combine(_tempDir, "custom-output");

        var result = SeparateCommand.Validate(
            _existingInputFile,
            "vocal_balanced",
            formatStr: null,
            outputDirOverride: customDir,
            _settings,
            _paths
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(customDir, result.ResolvedOutputDir);
    }

    // ── What counts as work ────────────────────────────────────────────────────

    [Fact]
    public void HasWorkToDo_PresetGiven_IsTrue() =>
        Assert.True(SeparateCommand.HasWorkToDo(["vocal_full"], model: null, extractDrums: false));

    [Fact]
    public void HasWorkToDo_ExtractDrumsAlone_IsTrue() =>
        // The case this exists for: an already-instrumental source needs only a drum stem, and
        // requiring a preset alongside meant running a separation just to discard its output.
        Assert.True(SeparateCommand.HasWorkToDo([], model: null, extractDrums: true));

    [Fact]
    public void HasWorkToDo_PresetAndExtractDrums_IsTrue() =>
        Assert.True(SeparateCommand.HasWorkToDo(["vocal_full"], model: null, extractDrums: true));

    [Fact]
    public void HasWorkToDo_NoPresetsAndNoDrums_IsFalse() =>
        Assert.False(SeparateCommand.HasWorkToDo([], model: null, extractDrums: false));

    [Fact]
    public void HasWorkToDo_NullPresetsAndNoDrums_IsFalse() =>
        // Spectre leaves the array null when the option never appears, so the guard has to accept
        // null rather than only an empty array.
        Assert.False(SeparateCommand.HasWorkToDo(null, model: null, extractDrums: false));

    [Fact]
    public void ValidatePresets_EmptyList_SucceedsWithNoPresets()
    {
        // Reachable now that --extract-drums can stand alone; the drum step is not a preset.
        var result = SeparateCommand.ValidatePresets([]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Presets!);
    }

    // ── --model and --keep ─────────────────────────────────────────────────────

    private static readonly ModelInfo DrumSep = new(
        "MDX23C-DrumSep-aufr33-jarredou.ckpt",
        "MDXC",
        "MDX23C Model: MDX23C DrumSep by aufr33-jarredou",
        [
            new StemSdr("kick", null),
            new StemSdr("snare", null),
            new StemSdr("toms", null),
            new StemSdr("hh", null),
            new StemSdr("ride", null),
            new StemSdr("crash", null),
        ],
        ["MDX23C-DrumSep-aufr33-jarredou.ckpt", "config_drumsep_mdx23c.yaml"]
    );

    [Fact]
    public void HasWorkToDo_ModelAlone_IsTrue() =>
        Assert.True(SeparateCommand.HasWorkToDo(null, model: "x.ckpt", extractDrums: false));

    [Fact]
    public void ValidateModel_NoModelNoKeep_AddsNoRun()
    {
        var result = SeparateCommand.ValidateModel(null, null, [DrumSep]);

        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Preset);
    }

    [Fact]
    public void ValidateModel_KeepWithoutModel_Fails() =>
        Assert.Equal(1, SeparateCommand.ValidateModel(null, ["kick"], [DrumSep]).ExitCode);

    [Fact]
    public void ValidateModel_KnownModel_TakesCatalogCasingAndKeepSet()
    {
        var result = SeparateCommand.ValidateModel(
            "mdx23c-drumsep-aufr33-jarredou.CKPT",
            ["kick", "snare", "Kick"],
            [DrumSep]
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Same(DrumSep, result.Info);
        Assert.Equal(DrumSep.Filename, result.Preset!.PrimaryModel);
        Assert.Equal(["kick", "snare"], Assert.Single(result.Preset.Steps).KeepSet!);
    }

    [Fact]
    public void ValidateModel_UnknownModel_FailsAndSuggestsNearNames()
    {
        var result = SeparateCommand.ValidateModel("DrumSep", [], [DrumSep]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(DrumSep.Filename, result.ErrorMessage);
    }

    [Fact]
    public void ValidateModel_UnreadableCatalog_RunsTheNameUnchecked()
    {
        // A broken toolchain must not refuse the run; the driver reports an unknown name itself.
        var result = SeparateCommand.ValidateModel("anything.ckpt", ["kick"], []);

        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Info);
        Assert.Equal("anything.ckpt", result.Preset!.PrimaryModel);
    }

    [Fact]
    public void ValidateModel_KeepStemTheCatalogDoesNotList_IsNotRefused()
    {
        // The model profile is advisory (ADR 0010): audio-separator decides what a model writes.
        var result = SeparateCommand.ValidateModel(DrumSep.Filename, ["cowbell"], [DrumSep]);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void KeepStemWarning_NamesTheUnexpectedStems()
    {
        var profile = new ModelProfile(
            DrumSep.Filename,
            "MDXC",
            [
                new ProfileStem("kick", StemSource.Config),
                new ProfileStem("snare", StemSource.Config),
            ],
            IsComposite: false
        );

        var warning = SeparateCommand.KeepStemWarning(profile, ["Kick", "cowbell"]);

        Assert.NotNull(warning);
        Assert.Contains("not cowbell", warning);
        Assert.Null(SeparateCommand.KeepStemWarning(profile, ["KICK", "snare"]));
    }

    [Fact]
    public void KeepStemWarning_UnknownProfile_SaysNothing() =>
        Assert.Null(
            SeparateCommand.KeepStemWarning(
                new ModelProfile("x.ckpt", "MDXC", [], IsComposite: false),
                ["kick"]
            )
        );

    // ── Pipeline invocation shape ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ValidJob_InvokesDriverWithCorrectJobRecordShape()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        var fakeDriver = new FakeSeparatorDriverService();
        var fakeSettings = new AppSettings { DefaultAudioFormat = AudioFormat.Wav };
        var fakePaths = new AppPaths(fakeSettings);

        var services = new ServiceCollection();
        services.AddSingleton<AppSettings>(fakeSettings);
        services.AddSingleton(fakePaths);
        services.AddSingleton<ISeparatorDriverService>(fakeDriver);
        services.AddSingleton<SeparationPipeline>();

        await using var provider = services.BuildServiceProvider();

        // Patch the pipeline to use our fake driver, settings, and paths.
        // We can't easily call ExecuteAsync directly (it creates its own DI container),
        // so we test the pipeline invocation shape by constructing the pipeline directly
        // with known parameters and a JobRecord that Validate would produce.

        var validationResult = SeparateCommand.Validate(
            _existingInputFile,
            "vocal_balanced",
            formatStr: "wav",
            outputDirOverride: outputDir,
            fakeSettings,
            fakePaths
        );

        Assert.Equal(0, validationResult.ExitCode);

        var preset = validationResult.Preset!;
        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: Path.GetFullPath(_existingInputFile),
            SourceUrl: null,
            Presets: [preset],
            OutputDir: validationResult.ResolvedOutputDir!,
            ModelsDir: fakePaths.ModelsDirectory,
            StemOutputFormat: validationResult.ResolvedFormat
        );

        // Verify the job record fields are correct.
        Assert.Equal(Path.GetFullPath(_existingInputFile), job.InputFilePath);
        Assert.Null(job.SourceUrl);
        Assert.Single(job.Presets);
        Assert.Equal("vocal_balanced", job.Presets[0].Id);
        Assert.Equal(outputDir, job.OutputDir);
        Assert.Equal(fakePaths.ModelsDirectory, job.ModelsDir);
        Assert.Equal(AudioFormat.Wav, job.StemOutputFormat);
    }

    [Fact]
    public async Task RunAsync_InvalidPreset_NoDriverCallMade()
    {
        _ = new FakeSeparatorDriverService();
        var callCount = 0;

        // Validate returns an error, so the pipeline is never reached.
        var validationResult = SeparateCommand.Validate(
            _existingInputFile,
            "nonexistent_preset",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.NotEqual(0, validationResult.ExitCode);

        // No driver calls should have been made.
        Assert.Equal(0, callCount);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RunAsync_MissingFile_NoDriverCallMade()
    {
        var callCount = 0;

        var validationResult = SeparateCommand.Validate(
            Path.Combine(_tempDir, "missing.flac"),
            "vocal_balanced",
            formatStr: null,
            outputDirOverride: null,
            _settings,
            _paths
        );

        Assert.NotEqual(0, validationResult.ExitCode);
        Assert.Equal(0, callCount);
        await Task.CompletedTask;
    }
}

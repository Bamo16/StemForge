using Microsoft.Extensions.DependencyInjection;
using StemForge.Core.Models;
using StemForge.Core.Services;
using TFile = TagLib.File;

namespace StemForge.Tests.Integration;

/// <summary>
/// End-to-end separation integration test. Builds the real shared service graph via
/// <see cref="CoreServiceExtensions.AddStemForgeCore"/>, resolves the <see cref="SeparationPipeline"/>
/// from the DI container (the same wiring the CLI and GUI use), and runs a built-in preset against
/// a small committed audio fixture. The pipeline drives the actual separator toolchain, so this is
/// SLOW and requires a provisioned environment; it is excluded from the default run via
/// <see cref="IntegrationGate"/> and runs only when <c>STEMFORGE_INTEGRATION=1</c> is set.
///
/// Asserts on the real outputs: file count, the title-prefixed naming the pipeline applies, the
/// requested extension, and that the stem duration (read back with TagLibSharp) matches the
/// fixture. Failure points clearly at separation behavior, separate from the download path.
/// </summary>
public sealed class SeparationIntegrationTests : IDisposable
{
    // Built-in preset id from PresetCatalog.BuiltIn; vocal_balanced emits a single Vocals stem.
    private const string PresetId = "vocal_balanced";

    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "sine-tone.flac"
    );

    private readonly string _outputDir;

    /// <summary>Referenced by <c>SkipUnless</c> (resolved on the test class): true only when the
    /// integration env gate is set.</summary>
    public static bool Enabled => IntegrationGate.Enabled;

    public SeparationIntegrationTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"sf-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outputDir, recursive: true);
        }
        catch { }
    }

    [Fact(SkipUnless = nameof(IntegrationGate.Enabled), Skip = IntegrationGate.SkipReason)]
    public async Task BuiltInPreset_AgainstFixture_WritesNamedStemWithExpectedDuration()
    {
        Assert.True(File.Exists(FixturePath), $"Fixture missing: {FixturePath}");

        // Build the real shared service graph and resolve the pipeline the same way the app does.
        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var paths = provider.GetRequiredService<AppPaths>();
        var pipeline = provider.GetRequiredService<SeparationPipeline>();

        var preset = PresetCatalog.BuiltIn.Single(p => p.Id == PresetId);

        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: FixturePath,
            SourceUrl: null,
            Presets: [preset],
            OutputDir: _outputDir,
            ModelsDir: paths.ModelsDirectory,
            StemOutputFormat: AudioFormat.Flac
        );

        var outputs = await pipeline.RunAsync(
            job,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        // Output count: one stem for this single-stem vocal preset.
        Assert.Single(outputs);

        var stemPath = outputs[0];
        Assert.True(File.Exists(stemPath), $"Reported output not on disk: {stemPath}");

        // Extension matches the requested FLAC format.
        Assert.Equal(".flac", Path.GetExtension(stemPath).ToLowerInvariant());

        // Naming: pipeline prefixes the source title and appends the preset label in parentheses.
        var fixtureTitle = Path.GetFileNameWithoutExtension(FixturePath);
        var stemName = Path.GetFileName(stemPath);
        Assert.StartsWith(fixtureTitle, stemName);
        Assert.Contains("(", stemName);
        Assert.Contains(")", stemName);

        // Duration: read the stem back with TagLibSharp and compare to the fixture length.
        using var fixtureFile = TFile.Create(FixturePath);
        using var stemFile = TFile.Create(stemPath);
        var expected = fixtureFile.Properties.Duration;
        var actual = stemFile.Properties.Duration;

        Assert.True(expected > TimeSpan.Zero, "Fixture should report a non-zero duration.");
        Assert.True(
            Math.Abs((actual - expected).TotalSeconds) < 0.5,
            $"Stem duration {actual} should match fixture duration {expected} within 0.5s."
        );
    }
}

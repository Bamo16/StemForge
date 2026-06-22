using Microsoft.Extensions.DependencyInjection;

namespace StemForge.Tests.Integration;

/// <summary>
/// End-to-end check that the lightweight catalog listing path works against a real provisioned
/// audio-separator environment. Resolves the catalog services from the shared DI container (the
/// same wiring the CLI and GUI use) and runs the tools/list_presets.py and tools/list_models.py
/// one-shots, asserting they return non-empty, parsed catalogs.
///
/// These scripts read audio_separator's static data files (ensemble_presets.json, models.json,
/// models-scores.json) without importing the audio_separator package, so no torch import happens.
/// Excluded from the default run via <see cref="IntegrationGate"/>; runs only when
/// <c>STEMFORGE_INTEGRATION=1</c> is set, because they require the provisioned Python toolchain.
/// </summary>
public sealed class LightweightListingIntegrationTests
{
    /// <summary>Referenced by <c>SkipUnless</c> (resolved on the test class): true only when the
    /// integration env gate is set.</summary>
    public static bool Enabled => IntegrationGate.Enabled;

    [Fact(SkipUnless = nameof(IntegrationGate.Enabled), Skip = IntegrationGate.SkipReason)]
    public async Task ListPresets_AgainstRealEnv_ReturnsKnownPresets()
    {
        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<PresetCatalogService>();

        var presets = await catalog.ListPresetsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(presets);
        // vocal_balanced is a stable built-in preset id shipped in ensemble_presets.json.
        var vocal = presets.Single(p => p.Id == "vocal_balanced");
        Assert.NotEmpty(vocal.AllModels);
        Assert.False(string.IsNullOrWhiteSpace(vocal.EnsembleAlgorithm));
    }

    [Fact(SkipUnless = nameof(IntegrationGate.Enabled), Skip = IntegrationGate.SkipReason)]
    public async Task ListModels_AgainstRealEnv_ReturnsModelsWithStems()
    {
        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<ModelCatalogService>();

        var models = await catalog.ListModelsAsync(
            forceRefresh: true,
            ct: TestContext.Current.CancellationToken
        );

        Assert.NotEmpty(models);
        // Every parsed model carries a filename. Stems come from the bundled score data, which
        // does not cover every model, so an empty stem list is valid; only the filename is required.
        Assert.All(models, m => Assert.False(string.IsNullOrWhiteSpace(m.Filename)));
        // At least some models do carry stem/score data, proving the merge wired scores through.
        Assert.Contains(models, m => m.Stems.Count > 0);
    }
}

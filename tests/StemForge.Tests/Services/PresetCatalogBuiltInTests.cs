using StemForge.Core.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Guards the built-in fallback preset catalog: every ensemble (2+ models) must carry an
/// ensemble algorithm so the preset card can show its algorithm chip before the driver's
/// live <c>list_presets</c> response arrives.
/// </summary>
public sealed class PresetCatalogBuiltInTests
{
    [Fact]
    public void EveryMultiModelBuiltIn_CarriesEnsembleAlgorithm()
    {
        var ensembles = PresetCatalog.BuiltIn.Where(p => p.ModelCount >= 2).ToList();

        Assert.NotEmpty(ensembles);
        Assert.All(
            ensembles,
            p =>
                Assert.False(
                    string.IsNullOrWhiteSpace(p.EnsembleAlgorithm),
                    $"built-in preset '{p.Id}' is an ensemble but has no EnsembleAlgorithm"
                )
        );
    }

    [Fact]
    public void EveryBuiltInAlgorithm_ResolvesToAKnownEntry()
    {
        var ensembles = PresetCatalog.BuiltIn.Where(p => p.EnsembleAlgorithm is not null);

        Assert.All(
            ensembles,
            p =>
            {
                var info = EnsembleAlgorithmCatalog.Resolve(p.EnsembleAlgorithm);
                // Known entries have a description distinct from the raw key; an unknown key
                // would echo itself back. Built-ins must all be known.
                Assert.NotEqual(info.Key, info.Description);
            }
        );
    }

    [Theory]
    [InlineData("vocal_balanced", "avg_fft")]
    [InlineData("vocal_clean", "min_fft")]
    [InlineData("vocal_full", "max_fft")]
    [InlineData("vocal_rvc", "avg_wave")]
    [InlineData("instrumental_balanced", "uvr_max_spec")]
    [InlineData("instrumental_low_resource", "avg_fft")]
    [InlineData("karaoke", "avg_wave")]
    public void BuiltIn_MatchesLiveCatalogAlgorithm(string id, string expectedAlgorithm)
    {
        var preset = PresetCatalog.BuiltIn.Single(p => p.Id == id);
        Assert.Equal(expectedAlgorithm, preset.EnsembleAlgorithm);
    }
}

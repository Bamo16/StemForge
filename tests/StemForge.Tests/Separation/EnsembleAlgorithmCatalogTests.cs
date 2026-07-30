namespace StemForge.Tests.Separation;

public sealed class EnsembleAlgorithmCatalogTests
{
    [Theory]
    [InlineData("avg_wave", "Averaged")]
    [InlineData("median_wave", "Median")]
    [InlineData("min_fft", "Min FFT")]
    [InlineData("max_fft", "Max FFT")]
    [InlineData("avg_fft", "Mean FFT")]
    [InlineData("uvr_max_spec", "Max spectrum")]
    [InlineData("uvr_min_spec", "Min spectrum")]
    public void Resolve_KnownKey_ReturnsHumanLabelAndDescription(string key, string expectedLabel)
    {
        var info = EnsembleAlgorithmCatalog.Resolve(key);

        Assert.Equal(key, info.Key);
        Assert.Equal(expectedLabel, info.Label);
        Assert.NotEqual(key, info.Description); // a real description, not the raw key
        Assert.NotEmpty(info.Description);
    }

    [Fact]
    public void Resolve_MeanFftAlias_ResolvesToCanonicalAvgFft()
    {
        var alias = EnsembleAlgorithmCatalog.Resolve("mean_fft");
        var canonical = EnsembleAlgorithmCatalog.Resolve("avg_fft");

        Assert.Equal("avg_fft", alias.Key);
        Assert.Equal(canonical.Label, alias.Label);
        Assert.Equal(canonical.Description, alias.Description);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var info = EnsembleAlgorithmCatalog.Resolve("AVG_WAVE");
        Assert.Equal("avg_wave", info.Key);
        Assert.Equal("Averaged", info.Label);
    }

    [Fact]
    public void Resolve_UnknownKey_FallsBackToRawKeyWithoutError()
    {
        var info = EnsembleAlgorithmCatalog.Resolve("some_future_algo");

        Assert.Equal("some_future_algo", info.Key);
        Assert.Equal("some_future_algo", info.Label);
        Assert.Equal("some_future_algo", info.Description);
    }

    [Fact]
    public void Resolve_TrimsSurroundingWhitespaceOnUnknownKey()
    {
        var info = EnsembleAlgorithmCatalog.Resolve("  weird_algo  ");
        Assert.Equal("weird_algo", info.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrBlankKey_ReturnsEmptyInfo(string? key)
    {
        var info = EnsembleAlgorithmCatalog.Resolve(key);

        Assert.Equal(string.Empty, info.Key);
        Assert.Equal(string.Empty, info.Label);
        Assert.Equal(string.Empty, info.Description);
    }

    [Fact]
    public void Known_ContainsTheRealCatalogAlgorithmKeys()
    {
        var keys = EnsembleAlgorithmCatalog.Known.Select(a => a.Key).ToHashSet();

        // Every algorithm the live built-in catalog emits must be a known entry.
        foreach (var k in new[] { "avg_fft", "min_fft", "max_fft", "avg_wave", "uvr_max_spec" })
            Assert.Contains(k, keys);
    }
}

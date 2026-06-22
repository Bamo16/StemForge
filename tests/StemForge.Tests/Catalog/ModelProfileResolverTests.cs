using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Catalog;

/// <summary>
/// Covers every confidence tier of <see cref="ModelProfileResolver"/>: config/benchmark stems,
/// config-on-demand fetch, the Demucs four-stem default, MDX/VR target+complement, the filename
/// fallback, and UNKNOWN. Each test names the tier it pins and asserts both the stem names and the
/// <see cref="StemSource"/> tag, so the confidence ordering is verifiable by a reader.
/// </summary>
public sealed class ModelProfileResolverTests
{
    private static ModelInfo Model(
        string filename,
        string arch,
        params (string Name, double? Sdr)[] stems
    ) => new(filename, arch, filename, stems.Select(s => new StemSdr(s.Name, s.Sdr)).ToList());

    // ── Tier 1: config / benchmark instrument list ─────────────────────────────

    [Fact]
    public async Task Resolve_ModelWithBenchmarkStems_UsesThemAtConfigConfidence()
    {
        // A roformer/MDXC model whose stems come from the bundled benchmark data: highest tier,
        // tagged Config, and no architecture default or filename guessing should occur.
        var resolver = new ModelProfileResolver();
        var model = Model(
            "bs_roformer_vocals.ckpt",
            "MDXC",
            ("vocals", 12.9),
            ("instrumental", 16.9)
        );

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(["vocals", "instrumental"], profile.Stems.Select(s => s.Name));
        Assert.All(profile.Stems, s => Assert.Equal(StemSource.Config, s.Source));
        Assert.Equal(StemSource.Config, profile.Confidence);
        Assert.False(profile.IsUnknown);
    }

    [Fact]
    public async Task Resolve_ConfigDrivenModelNoBenchmarkStems_FetchesConfigOnDemand()
    {
        // MDXC model the benchmark lists no stems for. The resolver must reach the config seam
        // (config only, never weights) and prefer that over the architecture default.
        var config = new FakeModelConfigSource(["vocals", "drums", "bass", "other", "guitar"]);
        var resolver = new ModelProfileResolver(config);
        var model = Model("some_mdxc_model.ckpt", "MDXC");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(1, config.CallCount);
        Assert.Equal(
            ["vocals", "drums", "bass", "other", "guitar"],
            profile.Stems.Select(s => s.Name)
        );
        Assert.All(profile.Stems, s => Assert.Equal(StemSource.Config, s.Source));
    }

    [Fact]
    public async Task Resolve_ModelWithBenchmarkStems_NeverFetchesConfig()
    {
        // When benchmark stems exist the config seam must NOT be hit — config-on-demand is lazy.
        var config = new FakeModelConfigSource(["should", "not", "be", "used"]);
        var resolver = new ModelProfileResolver(config);
        var model = Model("kim_vocal.ckpt", "MDXC", ("vocals", 10.0), ("instrumental", 9.0));

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(0, config.CallCount);
        Assert.Equal(["vocals", "instrumental"], profile.Stems.Select(s => s.Name));
    }

    // ── Tier 2: architecture defaults ──────────────────────────────────────────

    [Fact]
    public async Task Resolve_DemucsNoStems_FillsFourStemDefault()
    {
        var resolver = new ModelProfileResolver();
        var model = Model("htdemucs.yaml", "Demucs");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(["vocals", "drums", "bass", "other"], profile.Stems.Select(s => s.Name));
        Assert.All(profile.Stems, s => Assert.Equal(StemSource.ArchitectureDefault, s.Source));
        Assert.Equal(StemSource.ArchitectureDefault, profile.Confidence);
    }

    [Fact]
    public async Task Resolve_MdxVocalModelNoStems_ResolvesTargetPlusComplement()
    {
        // MDX is two-stem: the target inferred from the filename plus its complement, both tagged
        // ArchitectureDefault.
        var resolver = new ModelProfileResolver();
        var model = Model("UVR-MDX-NET-Voc_FT.onnx", "MDX");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(["vocals", "instrumental"], profile.Stems.Select(s => s.Name));
        Assert.All(profile.Stems, s => Assert.Equal(StemSource.ArchitectureDefault, s.Source));
    }

    [Fact]
    public async Task Resolve_VrInstrumentalModelNoStems_ComplementIsVocals()
    {
        var resolver = new ModelProfileResolver();
        var model = Model("9_HP2-UVR-instrumental.pth", "VR");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(["instrumental", "vocals"], profile.Stems.Select(s => s.Name));
        Assert.All(profile.Stems, s => Assert.Equal(StemSource.ArchitectureDefault, s.Source));
    }

    [Fact]
    public async Task Resolve_MdxNonVocalTarget_ComplementIsNamedNoTarget()
    {
        // A non-vocals two-stem target gets a "no <target>" complement.
        var resolver = new ModelProfileResolver();
        var model = Model("UVR-MDX-crowd-removal.onnx", "MDX");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(["crowd", "no crowd"], profile.Stems.Select(s => s.Name));
    }

    // ── Tier 3: filename-derived target (last resort) ──────────────────────────

    [Fact]
    public async Task Resolve_UnknownArchWithFilenameTarget_UsesFilenameTargetOnly()
    {
        // An architecture with no default but a recognisable filename target: a single stem tagged
        // FilenameTarget, no complement (lowest non-unknown confidence).
        var resolver = new ModelProfileResolver();
        var model = Model("mystery_drums_model.bin", "Unknown");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(["drums"], profile.Stems.Select(s => s.Name));
        Assert.Equal(StemSource.FilenameTarget, profile.Stems.Single().Source);
        Assert.Equal(StemSource.FilenameTarget, profile.Confidence);
    }

    // ── Unknown ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_NoStemsNoArchNoFilenameHint_ReportsUnknown()
    {
        // Absent from benchmark data, no architecture default, no filename hint: UNKNOWN, not a
        // failure.
        var resolver = new ModelProfileResolver();
        var model = Model("opaque_model_xyz.bin", "Unknown");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.True(profile.IsUnknown);
        Assert.Empty(profile.Stems);
        Assert.Equal(StemSource.Unknown, profile.Confidence);
    }

    [Fact]
    public async Task Resolve_ConfigDrivenNoStemsNoConfigSource_FallsThroughToFilename()
    {
        // No config source wired (the default DI state in v0.3.0): a config-driven model with no
        // benchmark stems must still resolve via the cheaper tiers rather than throwing.
        var resolver = new ModelProfileResolver();
        var model = Model("bs_roformer_vocals_unwa.ckpt", "MDXC");

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        // No two-stem default for MDXC, so it lands on the filename target tier.
        Assert.Equal(["vocals"], profile.Stems.Select(s => s.Name));
        Assert.Equal(StemSource.FilenameTarget, profile.Confidence);
    }

    // ── Composite (bag) flag ───────────────────────────────────────────────────

    [Theory]
    [InlineData("htdemucs_ft.yaml", true)]
    [InlineData("htdemucs_6s.yaml", true)]
    [InlineData("htdemucs.yaml", false)]
    [InlineData("kim_vocal_2.onnx", false)]
    public async Task Resolve_SetsCompositeFlagFromFilename(string filename, bool expected)
    {
        var resolver = new ModelProfileResolver();
        var model = Model(filename, "Demucs", ("vocals", 1.0));

        var profile = await resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(expected, profile.IsComposite);
    }
}

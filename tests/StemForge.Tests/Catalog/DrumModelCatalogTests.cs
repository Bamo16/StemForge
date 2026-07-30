namespace StemForge.Tests.Catalog;

/// <summary>
/// Pins the objective rule behind the drum-extraction model picker (issue #70): a model is offered
/// only when its resolved <see cref="ModelProfile"/> emits a "drums" stem. Profiles are built with
/// the real <see cref="ModelProfileResolver"/> so the test exercises the same resolution the picker
/// uses, then asserts <see cref="DrumModelCatalog.EmitsDrums"/> includes/excludes correctly.
///
/// Covers the three acceptance cases: a known drums-producing model IS listed, a vocals-only model is
/// EXCLUDED, and an unknown-stem model is EXCLUDED.
/// </summary>
public sealed class DrumModelCatalogTests
{
    private static ModelInfo Model(
        string filename,
        string arch,
        params (string Name, double? Sdr)[] stems
    ) => new(filename, arch, filename, stems.Select(s => new StemSdr(s.Name, s.Sdr)).ToList());

    private static async Task<ModelProfile> ResolveAsync(ModelInfo model) =>
        await new ModelProfileResolver().ResolveAsync(model, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EmitsDrums_DemucsFourStemModel_IsListed()
    {
        // Demucs emits drums in its fixed four-stem set: a known drums-producing model must qualify.
        var profile = await ResolveAsync(Model("htdemucs.yaml", "Demucs"));

        Assert.True(DrumModelCatalog.EmitsDrums(profile));
    }

    [Fact]
    public async Task EmitsDrums_ModelWithBenchmarkDrumsStem_IsListed()
    {
        // A model whose benchmark/config stem list explicitly includes drums also qualifies.
        var profile = await ResolveAsync(
            Model("six_stem.ckpt", "MDXC", ("vocals", 1.0), ("drums", 2.0), ("bass", 3.0))
        );

        Assert.True(DrumModelCatalog.EmitsDrums(profile));
    }

    [Fact]
    public async Task EmitsDrums_VocalsOnlyModel_IsExcluded()
    {
        // A two-stem vocals/instrumental model produces no drums and must NOT be offered.
        var profile = await ResolveAsync(
            Model("Kim_Vocal_2.onnx", "MDX", ("vocals", 10.0), ("instrumental", 9.0))
        );

        Assert.False(DrumModelCatalog.EmitsDrums(profile));
    }

    [Fact]
    public async Task EmitsDrums_UnknownStemModel_IsExcluded()
    {
        // No benchmark stems, no architecture default, no filename hint: an UNKNOWN profile is never
        // a drum model, so the large set of unknown-stem models stays out of the picker.
        var profile = await ResolveAsync(Model("opaque_model_xyz.bin", "Unknown"));

        Assert.True(profile.IsUnknown);
        Assert.False(DrumModelCatalog.EmitsDrums(profile));
    }

    [Fact]
    public async Task EmitsDrums_DrumsStemNameIsCaseInsensitive()
    {
        var profile = await ResolveAsync(Model("loud_drums.ckpt", "MDXC", ("DRUMS", 5.0)));

        Assert.True(DrumModelCatalog.EmitsDrums(profile));
    }
}

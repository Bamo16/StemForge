using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class ModelItemViewModelTests
{
    private static readonly ModelInfo Sw = new(
        "BS-Roformer-SW.ckpt",
        "MDXC",
        "Roformer Model: BS Roformer SW by jarredou",
        [],
        ["BS-Roformer-SW.ckpt", "BS-Roformer-SW.yaml"]
    );

    [Fact]
    public void StemsAreInferred_StemsReadFromConfig_IsFalse()
    {
        var profile = Profile(StemSource.Config, "bass", "drums");

        var item = new ModelItemViewModel(Sw, profile);

        Assert.False(item.StemsAreInferred);
        Assert.Equal("bass, drums", item.StemNames);
    }

    [Theory]
    [InlineData(StemSource.FilenameTarget)]
    [InlineData(StemSource.ArchitectureDefault)]
    public void StemsAreInferred_StemsGuessed_IsTrue(StemSource source)
    {
        var item = new ModelItemViewModel(Sw, Profile(source, "vocals", "instrumental"));

        Assert.True(item.StemsAreInferred);
    }

    private static ModelProfile Profile(StemSource source, params string[] stems) =>
        new(
            Sw.Filename,
            Sw.Architecture,
            [.. stems.Select(stem => new ProfileStem(stem, source))],
            false
        );
}

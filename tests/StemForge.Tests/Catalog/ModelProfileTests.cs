namespace StemForge.Tests.Catalog;

public sealed class ModelProfileTests
{
    private static ModelProfile Profile(params ProfileStem[] stems) =>
        new("model.ckpt", "MDX", stems, IsComposite: false);

    [Fact]
    public void Confidence_MixedSources_IsTheLowestOne() =>
        Assert.Equal(
            StemSource.FilenameTarget,
            Profile(
                new("vocals", StemSource.Config),
                new("instrumental", StemSource.FilenameTarget),
                new("drums", StemSource.ArchitectureDefault)
            ).Confidence
        );

    [Fact]
    public void Confidence_AllFromConfig_IsConfig() =>
        Assert.Equal(
            StemSource.Config,
            Profile(new("vocals", StemSource.Config), new("other", StemSource.Config)).Confidence
        );

    [Fact]
    public void Confidence_NoStems_IsUnknownAndTheProfileIsUnknown()
    {
        var profile = Profile();

        Assert.Equal(StemSource.Unknown, profile.Confidence);
        Assert.True(profile.IsUnknown);
    }

    [Fact]
    public void IsUnknown_AnyResolvedStem_IsFalse() =>
        Assert.False(Profile(new ProfileStem("vocals", StemSource.FilenameTarget)).IsUnknown);

    [Fact]
    public void StemSource_OrderedByDescendingConfidence()
    {
        Assert.True(StemSource.Config > StemSource.ArchitectureDefault);
        Assert.True(StemSource.ArchitectureDefault > StemSource.FilenameTarget);
        Assert.True(StemSource.FilenameTarget > StemSource.Unknown);
    }
}

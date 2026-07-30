using StemForge.Core.Catalog;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

/// <summary>
/// Pins the ensemble stem-overlap aggregation (issue #69): audio-separator averages stem names
/// emitted by 2+ selected models and passes through names emitted by a single model. These tests
/// assert the contributor counts and the averaged/passthrough split, plus that unknown-stem models
/// degrade gracefully (surfaced, never counted).
/// </summary>
public sealed class EnsembleOverlapTests
{
    /// <summary>Builds a profile with the given stem names at Config confidence.</summary>
    private static ModelProfile Profile(string filename, params string[] stems) =>
        new(
            filename,
            "MDXC",
            stems.Select(s => new ProfileStem(s, StemSource.Config)).ToList(),
            IsComposite: false
        );

    /// <summary>An UNKNOWN profile — resolved no stems.</summary>
    private static ModelProfile UnknownProfile(string filename) =>
        new(filename, "MDX", [], IsComposite: false);

    [Fact]
    public void TwoModelsSharingAStem_IsAveraged()
    {
        // Both models emit "vocals"; audio-separator averages that group of two.
        var result = EnsembleOverlap.Aggregate([
            ("Model A", Profile("a.ckpt", "vocals", "instrumental")),
            ("Model B", Profile("b.ckpt", "vocals", "other")),
        ]);

        var vocals = Assert.Single(result.Averaged);
        Assert.Equal("vocals", vocals.Name);
        Assert.Equal(2, vocals.ContributorCount);
        Assert.True(vocals.IsAveraged);
        Assert.False(vocals.IsPassthrough);
        Assert.True(result.HasAveraged);
    }

    [Fact]
    public void StemFromOneModel_PassesThrough()
    {
        // "instrumental" and "other" each come from a single model: passthrough, not averaged.
        var result = EnsembleOverlap.Aggregate([
            ("Model A", Profile("a.ckpt", "vocals", "instrumental")),
            ("Model B", Profile("b.ckpt", "vocals", "other")),
        ]);

        Assert.Equal(["instrumental", "other"], result.Passthrough.Select(s => s.Name));
        Assert.All(result.Passthrough, s => Assert.Equal(1, s.ContributorCount));
        Assert.All(result.Passthrough, s => Assert.True(s.IsPassthrough));
    }

    [Fact]
    public void StemNamesAreMatchedCaseInsensitively()
    {
        // "Vocals" and "vocals" are the same stem name — two contributors, averaged.
        var result = EnsembleOverlap.Aggregate([
            ("Model A", Profile("a.ckpt", "Vocals")),
            ("Model B", Profile("b.ckpt", "vocals")),
        ]);

        var vocals = Assert.Single(result.Stems);
        Assert.Equal(2, vocals.ContributorCount);
        Assert.True(vocals.IsAveraged);
    }

    [Fact]
    public void DuplicateStemNameWithinOneModel_CountsOnce()
    {
        // A single model listing "vocals" twice must not self-average; it is one contributor.
        var result = EnsembleOverlap.Aggregate([
            ("Model A", Profile("a.ckpt", "vocals", "vocals")),
        ]);

        var vocals = Assert.Single(result.Stems);
        Assert.Equal(1, vocals.ContributorCount);
        Assert.True(vocals.IsPassthrough);
    }

    [Fact]
    public void UnknownStemModel_IsSurfacedAndNotCounted()
    {
        // The unknown model must NOT count toward "vocals" (which would falsely make it averaged),
        // and must be surfaced so it is not silently dropped.
        var result = EnsembleOverlap.Aggregate([
            ("Known", Profile("known.ckpt", "vocals")),
            ("Mystery", UnknownProfile("mystery.ckpt")),
        ]);

        var vocals = Assert.Single(result.Stems);
        Assert.Equal(1, vocals.ContributorCount);
        Assert.True(vocals.IsPassthrough);

        Assert.Equal(["Mystery"], result.UnknownModels);
        Assert.True(result.HasUnknownModels);
    }

    [Fact]
    public void NullProfile_IsTreatedAsUnknown()
    {
        // A missing profile (resolver returned nothing) degrades the same way as an unknown one.
        var result = EnsembleOverlap.Aggregate([("No Profile", (ModelProfile?)null)]);

        Assert.Empty(result.Stems);
        Assert.Equal(["No Profile"], result.UnknownModels);
    }

    [Fact]
    public void TwoUnknownModels_DoNotInventAnAveragedStem()
    {
        // Critical degrade case: two unknowns must not be aggregated into any blended stem.
        var result = EnsembleOverlap.Aggregate([
            ("Mystery A", UnknownProfile("a.ckpt")),
            ("Mystery B", UnknownProfile("b.ckpt")),
        ]);

        Assert.Empty(result.Stems);
        Assert.False(result.HasAveraged);
        Assert.Equal(["Mystery A", "Mystery B"], result.UnknownModels);
    }

    [Fact]
    public void StemsAreOrderedByNameForStableDisplay()
    {
        var result = EnsembleOverlap.Aggregate([
            ("A", Profile("a.ckpt", "vocals", "bass")),
            ("B", Profile("b.ckpt", "drums", "vocals")),
        ]);

        Assert.Equal(["bass", "drums", "vocals"], result.Stems.Select(s => s.Name));
    }

    [Fact]
    public void AveragedDisplay_ShowsNameAndCount()
    {
        var stem = new StemOverlap("vocals", 3);
        Assert.Equal("vocals (3 models)", stem.AveragedDisplay);
    }
}

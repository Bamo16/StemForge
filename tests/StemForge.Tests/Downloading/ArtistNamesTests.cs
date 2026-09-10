using StemForge.Core.Downloading;

namespace StemForge.Tests.Downloading;

public sealed class ArtistNamesTests
{
    // ── Canonical: collapsing YouTube Music's per-role repeats ───────────────

    /// <summary>
    /// The shape that motivated this: music.youtube.com/watch?v=dBLzh47wdrU credits three artists
    /// but reports eleven entries, one per role held.
    /// </summary>
    [Fact]
    public void Canonical_RepeatedEntries_CollapsesToUniqueNamesInFirstSeenOrder()
    {
        string[] artists =
        [
            "Jamie Paige",
            "OK Glass",
            "Jamie Paige",
            "OK Glass",
            "OK Glass",
            "ash taylor",
            "Jamie Paige",
            "Jamie Paige",
            "OK Glass",
            "OK Glass",
            "OK Glass",
        ];

        Assert.Equal(
            ["Jamie Paige", "OK Glass", "ash taylor"],
            ArtistNames.Canonical(artists, null)
        );
    }

    [Fact]
    public void Canonical_CasingDiffers_CollapsesAndKeepsFirstSpelling()
    {
        string[] artists = ["OK Glass", "ok glass", "OK GLASS"];
        Assert.Equal(["OK Glass"], ArtistNames.Canonical(artists, null));
    }

    [Fact]
    public void Canonical_NoArtistsArray_FallsBackToSplittingFlattenedString()
    {
        Assert.Equal(
            ["Jamie Paige", "ash taylor"],
            ArtistNames.Canonical(null, "Jamie Paige, ash taylor, Jamie Paige")
        );
    }

    [Fact]
    public void Canonical_ArtistsArrayPresent_PrefersItOverFlattenedString()
    {
        string[] artists = ["Real Name"];
        Assert.Equal(["Real Name"], ArtistNames.Canonical(artists, "Wrong, Wrong"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Canonical_NoArtistKnown_ReturnsEmpty(string? flattened)
    {
        // An empty credit list is what marks a source as an ordinary upload rather than a music
        // track entity, so a blank credit must not survive as an entry.
        Assert.Empty(ArtistNames.Canonical(null, flattened));
    }

    // ── WithoutFeatured: dropping a credit the title already carries ─────────

    [Theory]
    [InlineData("BIRDBRAIN (feat. OK Glass)")]
    [InlineData("BIRDBRAIN [feat. OK Glass]")]
    [InlineData("BIRDBRAIN feat. OK Glass")]
    [InlineData("BIRDBRAIN (ft. OK Glass)")]
    [InlineData("BIRDBRAIN (featuring OK Glass)")]
    [InlineData("BIRDBRAIN (FEAT. ok glass)")]
    public void WithoutFeatured_TitleCreditsArtist_DropsThatArtist(string title)
    {
        Assert.Equal(
            ["Jamie Paige", "ash taylor"],
            ArtistNames.WithoutFeatured(["Jamie Paige", "OK Glass", "ash taylor"], title)
        );
    }

    [Fact]
    public void WithoutFeatured_SeveralNamesInSegment_DropsEachOfThem()
    {
        Assert.Equal(
            ["Jamie Paige"],
            ArtistNames.WithoutFeatured(
                ["Jamie Paige", "OK Glass", "ash taylor"],
                "BIRDBRAIN (feat. OK Glass & ash taylor)"
            )
        );
    }

    /// <summary>
    /// The reason matching is confined to a feat segment and requires a whole name: a substring
    /// scan would find "Bird" inside "BIRDBRAIN" and silently drop a genuine artist.
    /// </summary>
    [Fact]
    public void WithoutFeatured_ArtistNameIsSubstringOfTitle_KeepsThatArtist()
    {
        Assert.Equal(["Bird"], ArtistNames.WithoutFeatured(["Bird"], "BIRDBRAIN (feat. OK Glass)"));
    }

    [Fact]
    public void WithoutFeatured_NameAppearsOutsideAFeatSegment_KeepsThatArtist()
    {
        // Credited in the title, but not as a featured performer: still the primary artist.
        Assert.Equal(
            ["Jamie Paige"],
            ArtistNames.WithoutFeatured(["Jamie Paige"], "Jamie Paige Live At Wembley")
        );
    }

    [Fact]
    public void WithoutFeatured_BracketedSegmentFollowedByAnother_StopsAtTheBracket()
    {
        Assert.Equal(
            ["Jamie Paige"],
            ArtistNames.WithoutFeatured(
                ["Jamie Paige", "OK Glass"],
                "BIRDBRAIN (feat. OK Glass) (Remix)"
            )
        );
    }

    [Fact]
    public void WithoutFeatured_MarkerIsPartOfALongerWord_DoesNotMatch()
    {
        // "Feature" must not read as a feat marker.
        Assert.Equal(["Feature"], ArtistNames.WithoutFeatured(["Feature"], "Double Feature"));
    }

    /// <summary>
    /// A credited name may itself contain what would otherwise be a separator. The credit list is
    /// carried as a list precisely so such a name is never re-parsed and broken in two.
    /// </summary>
    [Theory]
    [InlineData("Simon & Garfunkel")]
    [InlineData("Tyler, The Creator")]
    [InlineData("Emerson, Lake and Palmer")]
    public void WithoutFeatured_ArtistNameContainsASeparator_KeepsItWhole(string band)
    {
        Assert.Equal(
            [band],
            ArtistNames.WithoutFeatured([band, "Someone"], "Song (feat. Someone)")
        );
    }

    /// <summary>
    /// A featured credit whose own name contains a separator. Splitting the segment alone would
    /// look for "Tyler" and "The Creator" and match neither, so the whole segment is offered as a
    /// candidate too and the artist list decides which reading is real.
    /// </summary>
    [Theory]
    [InlineData("Song (feat. Tyler, The Creator)")]
    [InlineData("Song (feat. Simon & Garfunkel)")]
    [InlineData("Song feat. Emerson, Lake and Palmer")]
    public void WithoutFeatured_FeaturedNameContainsASeparator_DropsIt(string title)
    {
        var band = title[(title.IndexOf('.') + 1)..].TrimEnd(')').Trim();
        Assert.Equal(["Someone"], ArtistNames.WithoutFeatured([band, "Someone"], title));
    }

    /// <summary>
    /// The same segment read the other way: two separately credited artists rather than one name
    /// containing a comma. Both readings are offered, so the artist list settles it either way.
    /// </summary>
    [Fact]
    public void WithoutFeatured_SegmentIsTwoSeparateArtists_DropsBoth()
    {
        Assert.Equal(
            ["Jamie Paige"],
            ArtistNames.WithoutFeatured(
                ["Jamie Paige", "Tyler", "The Creator"],
                "Song (feat. Tyler, The Creator)"
            )
        );
    }

    [Fact]
    public void WithoutFeatured_EveryArtistIsFeatured_KeepsThemAll()
    {
        // Never leave a track artist-less: a bare title would lose the artist from the name.
        Assert.Equal(
            ["OK Glass"],
            ArtistNames.WithoutFeatured(["OK Glass"], "BIRDBRAIN (feat. OK Glass)")
        );
    }

    [Fact]
    public void WithoutFeatured_NoFeatSegment_ReturnsInputUnchanged()
    {
        Assert.Equal(
            ["Jamie Paige", "OK Glass"],
            ArtistNames.WithoutFeatured(["Jamie Paige", "OK Glass"], "BIRDBRAIN")
        );
    }

    [Fact]
    public void WithoutFeatured_NoArtists_ReturnsInputUnchanged()
    {
        Assert.Empty(ArtistNames.WithoutFeatured([], "BIRDBRAIN (feat. OK Glass)"));
    }
}

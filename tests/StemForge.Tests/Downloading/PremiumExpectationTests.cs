namespace StemForge.Tests.Downloading;

/// <summary>
/// The shortfall rule from ADR 0013. The format ids are the real ones: 141 and 774 are withheld
/// even from a signed-in free account, 250 is session-gated (shown to any signed-in account), and
/// 139/140/249/251 are offered to anyone.
/// </summary>
public sealed class PremiumExpectationTests
{
    // ── Expectation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("firefox")]
    [InlineData(@"C:\cookies.txt")]
    public void IsHeldBy_CookieSourceConfigured_ReturnsTrue(string cookies) =>
        Assert.True(
            PremiumExpectation.IsHeldBy(new AppSettings { YtdlpCookiesFromBrowser = cookies })
        );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsHeldBy_NoCookieSource_ReturnsFalse(string? cookies) =>
        Assert.False(
            PremiumExpectation.IsHeldBy(new AppSettings { YtdlpCookiesFromBrowser = cookies })
        );

    // ── Evaluation ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NoExpectation_IsNotApplicable()
    {
        // A user who never configured cookies is told nothing about premium formats, even
        // though this source plainly has none.
        var meta = Build("youtube", selected: "140", candidates: ["140", "251"]);
        Assert.Equal(
            PremiumStatus.NotApplicable,
            PremiumExpectation.Evaluate(meta, expectationHeld: false)
        );
    }

    [Fact]
    public void Evaluate_SelectedFormatBeatsTheFreeOnes_IsPremium()
    {
        var meta = Build("youtube", selected: "141", candidates: ["140", "251", "141", "774"]);
        Assert.Equal(
            PremiumStatus.Premium,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    [Fact]
    public void Evaluate_MusicTrackWithNothingGated_IsNotSignedIn()
    {
        // The logged-out format set. A signed-in free account would still have been shown the
        // session-gated rungs, so their total absence on a music track means no session at all.
        var meta = Build(
            "youtube",
            selected: "140",
            candidates: ["139", "249", "140", "251"],
            artist: "Nu:Tone"
        );
        Assert.Equal(
            PremiumStatus.NotSignedIn,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    [Fact]
    public void Evaluate_MusicTrackWithSessionGatedButNoPremium_IsAccountNotPremium()
    {
        // What a signed-in free account sees: 250 is session-gated and shown to it, while 141 and
        // 774 are withheld (0/552 in the corpus). All 326 music-typed sources carry both, so the
        // ladder exists and the account simply cannot reach it. Not a broken session.
        var meta = Build(
            "youtube",
            selected: "140",
            candidates: ["249", "250", "140", "251"],
            artist: "Nu:Tone"
        );
        Assert.Equal(
            PremiumStatus.AccountNotPremium,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    [Fact]
    public void Evaluate_VideoUploadWithNoGatedFormat_IsNoPremiumLadder()
    {
        // The 7-in-72 case that made a blanket rule warn falsely. An ordinary video upload was
        // never provisioned a ladder, so nothing here implicates the user's cookies. Both of the
        // real examples were official artist/label channels, so "official" is not the predictor.
        var meta = Build("youtube", selected: "140", candidates: ["139", "249", "140", "251"]);
        Assert.Equal(
            PremiumStatus.NoPremiumLadder,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    [Fact]
    public void IsMusicTrackEntity_KeysOnArtistMetadata()
    {
        Assert.True(Build("youtube", "140", ["140"], artist: "Nu:Tone").IsMusicTrackEntity);
        Assert.False(Build("youtube", "140", ["140"]).IsMusicTrackEntity);
        Assert.False(Build("youtube", "140", ["140"], artist: "  ").IsMusicTrackEntity);
    }

    [Fact]
    public void Evaluate_NonMusicSourceWithSessionGatedButNoPremium_IsSourceHasNoPremiumAudio()
    {
        // The music-video case. 250 is gated but 61 kbps, losing to the freely-available 140 at
        // 130 kbps, so this source has no premium audio to give. The session is provably alive,
        // so nothing here should point at the user's cookies.
        var meta = Build("youtube", selected: "140", candidates: ["249", "250", "140", "251"]);
        Assert.Equal(
            PremiumStatus.SourceHasNoPremiumAudio,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
        Assert.False(meta.SelectedFormatIsPremium);
        Assert.False(meta.OffersPremiumFormat);
        Assert.True(meta.HasAuthenticatedSessionEvidence);
    }

    [Theory]
    [InlineData("soundcloud")]
    [InlineData("bandcamp")]
    [InlineData(null)]
    public void Evaluate_NonYouTubeSource_IsNotApplicable(string? extractor)
    {
        // Format ids are per-extractor, so premium-ness is not inferable elsewhere. Holding a
        // premium expectation must not make every SoundCloud link look like a shortfall.
        var meta = Build(extractor, selected: "1", candidates: ["1", "2"]);
        Assert.Equal(
            PremiumStatus.NotApplicable,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    [Fact]
    public void Evaluate_ExtractorCasingIgnored()
    {
        var meta = Build("YouTube", selected: "140", candidates: ["140"], artist: "Nu:Tone");
        Assert.Equal(
            PremiumStatus.NotSignedIn,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    [Fact]
    public void Evaluate_NoCandidateList_IsNotSignedIn()
    {
        // A resolve that produced no candidate list cannot show evidence of authentication.
        // Reporting a shortfall is the honest outcome; it is also what a badly degraded
        // resolve looks like, which is exactly when the user wants to hear about it.
        var meta = Build("youtube", selected: "140", candidates: null, artist: "Nu:Tone");
        Assert.Equal(
            PremiumStatus.NotSignedIn,
            PremiumExpectation.Evaluate(meta, expectationHeld: true)
        );
    }

    /// <summary>
    /// Real bitrates for the ids observed on YouTube, so a candidate list built from ids alone
    /// reflects the actual quality ordering the premium rule depends on.
    /// </summary>
    private static readonly Dictionary<string, double> _bitrates = new()
    {
        ["139"] = 49,
        ["249"] = 46,
        ["250"] = 61, // gated, but below the free formats
        ["140"] = 130,
        ["251"] = 129,
        ["141"] = 258,
        ["774"] = 257,
    };

    private static YtDlpMetadata Build(
        string? extractor,
        string selected,
        string[]? candidates,
        string? artist = null
    ) =>
        new(
            SourceUrl: "https://www.youtube.com/watch?v=x",
            Title: "Track",
            Artist: artist,
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: selected,
            MediaUrl: "https://media.example.com/audio",
            AudioFormats: candidates
                ?.Select(id => new YtDlpFormat
                {
                    FormatId = id,
                    AverageAudioBitrate = _bitrates.GetValueOrDefault(id, 100),
                })
                .ToList(),
            Extractor: extractor
        );
}

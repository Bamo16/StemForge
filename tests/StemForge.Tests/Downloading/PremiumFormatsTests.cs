namespace StemForge.Tests.Downloading;

/// <summary>
/// "Premium" means a higher bitrate than the source offers without authentication, not merely
/// authentication-gated. The two diverge on real sources: format 250 is gated but 61 kbps, below
/// the 130 kbps anyone gets free, so badging it premium would promise quality that is not there.
/// </summary>
public sealed class PremiumFormatsTests
{
    // The authenticated format set for a typical music track.
    private static readonly YtDlpFormat[] _fullSet =
    [
        Fmt("249", 46),
        Fmt("250", 61), // gated, worse than free
        Fmt("140", 130),
        Fmt("251", 129),
        Fmt("141", 258), // gated, better than free
        Fmt("774", 257), // gated, better than free
    ];

    // The music-video case from real use: gated 250 present, but nothing beats free 140.
    private static readonly YtDlpFormat[] _noPremiumAudio =
    [
        Fmt("249", 52),
        Fmt("250", 68),
        Fmt("140", 130),
        Fmt("251", 132),
    ];

    [Theory]
    [InlineData("141")]
    [InlineData("774")]
    public void IsPremium_GatedAndBetterThanFree_ReturnsTrue(string formatId) =>
        Assert.True(PremiumFormats.IsPremium(Find(_fullSet, formatId), _fullSet, "youtube"));

    [Fact]
    public void IsPremium_GatedButWorseThanFree_ReturnsFalse() =>
        Assert.False(PremiumFormats.IsPremium(Find(_fullSet, "250"), _fullSet, "youtube"));

    [Theory]
    [InlineData("140")]
    [InlineData("251")]
    public void IsPremium_UngatedFormat_ReturnsFalse(string formatId) =>
        Assert.False(PremiumFormats.IsPremium(Find(_fullSet, formatId), _fullSet, "youtube"));

    [Fact]
    public void IsPremium_SourceWhoseOnlyGatedFormatIsWorseThanFree_HasNoPremiumFormats()
    {
        Assert.All(
            _noPremiumAudio,
            f => Assert.False(PremiumFormats.IsPremium(f, _noPremiumAudio, "youtube"))
        );
    }

    [Fact]
    public void AnyAuthGated_StillTrueWhenNoFormatQualifiesAsPremium()
    {
        // The distinction that keeps a fine session from being reported as a cookie problem.
        Assert.True(PremiumFormats.AnyAuthGated(_noPremiumAudio, "youtube"));
    }

    [Fact]
    public void AnyAuthGated_LoggedOutFormatSet_ReturnsFalse()
    {
        YtDlpFormat[] loggedOut =
        [
            Fmt("139", 49),
            Fmt("249", 46),
            Fmt("140", 130),
            Fmt("251", 129),
        ];
        Assert.False(PremiumFormats.AnyAuthGated(loggedOut, "youtube"));
    }

    [Fact]
    public void IsPremium_EveryFormatGated_TreatsAllAsImprovements()
    {
        // With no ungated format to compare against the bar is zero, so a gated format is by
        // definition better than what an unauthenticated request would have got: nothing.
        YtDlpFormat[] allGated = [Fmt("250", 61), Fmt("141", 258)];
        Assert.True(PremiumFormats.IsPremium(allGated[0], allGated, "youtube"));
        Assert.Equal(0, PremiumFormats.BestUngatedBitrate(allGated, "youtube"));
    }

    [Fact]
    public void IsPremium_NonYouTubeExtractor_ReturnsFalse() =>
        Assert.False(PremiumFormats.IsPremium(Find(_fullSet, "141"), _fullSet, "soundcloud"));

    [Fact]
    public void BestUngatedBitrate_IgnoresGatedFormats() =>
        Assert.Equal(130, PremiumFormats.BestUngatedBitrate(_fullSet, "youtube"));

    private static YtDlpFormat Fmt(string id, double kbps) =>
        new() { FormatId = id, AverageAudioBitrate = kbps };

    private static YtDlpFormat Find(YtDlpFormat[] set, string id) =>
        set.First(f => f.FormatId == id);
}

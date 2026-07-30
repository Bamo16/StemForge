using StemForge.Core.Separation.Models;

namespace StemForge.Tests.Separation;

/// <summary>
/// Pins the auth-gated itag set and the extractor guard. The set is empirical (YouTube publishes
/// no list) and the whole advisory rests on it, so a change here should be a deliberate act rather
/// than an incidental edit. Established across a 552-video corpus resolved logged out, signed-in
/// free, and signed-in Premium; see the comment on the set itself.
/// </summary>
public sealed class AudioFormatInfoTests
{
    [Theory]
    [InlineData("141")] // Premium-gated, AAC LC 258 kbps
    [InlineData("774")] // Premium-gated, Opus 257 kbps
    [InlineData("250")] // session-gated, Opus ~65 kbps: gated, but worse than the free formats
    public void IsAuthGated_GatedId_ReturnsTrue(string formatId) =>
        Assert.True(AudioFormatInfo.IsAuthGated(formatId, "youtube"));

    [Theory]
    [InlineData("139")] // HE-AAC, 49 kbps — offered anonymously, withdrawn on sign-in
    [InlineData("249")] // Opus, 46 kbps
    [InlineData("140")] // AAC LC, 130 kbps — the best an unauthenticated request gets
    [InlineData("251")] // Opus, 129 kbps
    public void IsAuthGated_FormatOfferedWithoutAuth_ReturnsFalse(string formatId) =>
        Assert.False(AudioFormatInfo.IsAuthGated(formatId, "youtube"));

    [Theory]
    [InlineData("140-drc")]
    [InlineData("251-9")]
    public void IsAuthGated_SuffixedVariantOfAnUngatedItag_ReturnsFalse(string formatId)
    {
        // These are session-gated in reality, but their bare itag is ungated so the set cannot
        // express them. Documented rather than fixed: it cannot happen on a music track (no DRC
        // or dubs were found on any Topic upload), and everywhere else it errs toward reporting
        // no session evidence, which is the safe direction. See the comment on the set.
        Assert.False(AudioFormatInfo.IsAuthGated(formatId, "youtube"));
    }

    [Fact]
    public void IsAuthGated_ExtractorIsCaseInsensitive() =>
        Assert.True(AudioFormatInfo.IsAuthGated("141", "YouTube"));

    [Theory]
    [InlineData("soundcloud")]
    [InlineData("bandcamp")]
    public void IsAuthGated_NonYouTubeExtractor_ReturnsFalse(string extractor)
    {
        // Format ids are only meaningful per-extractor: another site's "141" means something else
        // entirely, so premium-ness must never be inferred outside YouTube.
        Assert.False(AudioFormatInfo.IsAuthGated("141", extractor));
    }

    [Fact]
    public void IsAuthGated_NullFormatId_ReturnsFalse() =>
        Assert.False(AudioFormatInfo.IsAuthGated(null, "youtube"));

    [Fact]
    public void IsAuthGated_NullExtractor_ReturnsFalse() =>
        Assert.False(AudioFormatInfo.IsAuthGated("141", null));

    [Theory]
    [InlineData("mp4a.40.2", "AAC LC")]
    [InlineData("mp4a.40.5", "HE-AAC")]
    [InlineData("opus", "Opus")]
    [InlineData("flac", "FLAC")]
    public void PrettyCodec_KnownCode_ReturnsFriendlyName(string raw, string expected) =>
        Assert.Equal(expected, AudioFormatInfo.PrettyCodec(raw));

    [Fact]
    public void PrettyCodec_UnknownCode_PassesThroughUnchanged() =>
        Assert.Equal("ac-4", AudioFormatInfo.PrettyCodec("ac-4"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PrettyCodec_MissingCode_ReturnsEmpty(string? raw) =>
        Assert.Equal(string.Empty, AudioFormatInfo.PrettyCodec(raw));
}

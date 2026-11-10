using StemForge.Core.Downloading;

namespace StemForge.Tests.Downloading;

public sealed class TitlePackagingTests
{
    [Theory]
    [InlineData("Song (Official Video)", "Song")]
    [InlineData("Song (Official Music Video)", "Song")]
    [InlineData("Song [Official Audio]", "Song")]
    [InlineData("Song (Lyric Video)", "Song")]
    [InlineData("Song (Lyrics Video)", "Song")]
    [InlineData("Song (Official Lyric Video)", "Song")]
    [InlineData("Song (Official Visualizer)", "Song")]
    [InlineData("Song (Visualiser)", "Song")]
    [InlineData("Song (Audio Visualizer)", "Song")]
    [InlineData("Song (Official HD Video)", "Song")]
    [InlineData("Song (Official Video HD)", "Song")]
    [InlineData("Song (Music Video)", "Song")]
    [InlineData("Song (Lyrics)", "Song")]
    [InlineData("Song (MV)", "Song")]
    [InlineData("Song (OFFICIAL VIDEO)", "Song")]
    [InlineData("Song ( official video )", "Song")]
    public void Strip_PackagingPhrase_RemovesItAndTheSpaceBeforeIt(string title, string expected) =>
        Assert.Equal(expected, TitlePackaging.Strip(title));

    [Theory]
    [InlineData("Song (HD)", "Song")]
    [InlineData("Song [4K]", "Song")]
    [InlineData("Song (4K Remaster)", "Song")]
    [InlineData("Song (HD Remastered)", "Song")]
    [InlineData("Song (Remastered in 4K)", "Song")]
    [InlineData("Song [Official Video] [4K]", "Song")]
    public void Strip_ResolutionMarker_IsRemoved(string title, string expected) =>
        Assert.Equal(expected, TitlePackaging.Strip(title));

    [Theory]
    [InlineData("Song 【MV】", "Song")]
    [InlineData("Song【MV】", "Song")]
    [InlineData("【Official Video】Song", "Song")]
    [InlineData("Song 【Official Music Video】", "Song")]
    [InlineData("Song （Official Video）", "Song")]
    [InlineData("歌 【公式】", "歌 【公式】")]
    public void Strip_JapaneseBrackets_AreReadLikeAnyOther(string title, string expected) =>
        Assert.Equal(expected, TitlePackaging.Strip(title));

    [Theory]
    [InlineData("Song (Official Video [HD])", "Song")]
    [InlineData("Song [Official Video (4K)]", "Song")]
    [InlineData("Song (Remix (Official Video))", "Song (Remix)")]
    [InlineData("Song (Official Video) (Official Audio)", "Song")]
    public void Strip_NestedBrackets_ResolveFromTheInsideOut(string title, string expected) =>
        Assert.Equal(expected, TitlePackaging.Strip(title));

    [Theory]
    [InlineData("Song (feat. Artist)")]
    [InlineData("Song (Colin Hennerz Remix)")]
    [InlineData("Song (Japanese Version)")]
    [InlineData("Song (Live)")]
    [InlineData("Song (Live at Wembley)")]
    [InlineData("Song (Acoustic)")]
    [InlineData("Song (Extended Mix)")]
    [InlineData("Song (Remastered 2011)")]
    [InlineData("Song (Official Soundtrack)")]
    [InlineData("Song (Audio)")]
    [InlineData("Song [Explicit]")]
    [InlineData("Official Video Game Theme")]
    [InlineData("Song - Official Video")]
    public void Strip_PartOfTheSong_IsKept(string title) =>
        Assert.Equal(title, TitlePackaging.Strip(title));

    [Theory]
    [InlineData(
        "Paul Oakenfold - Starry Eyed Surprise (feat. Shifty Shellshock) (Official Video)",
        "Paul Oakenfold - Starry Eyed Surprise (feat. Shifty Shellshock)"
    )]
    [InlineData("Song (Official Video) (Colin Hennerz Remix)", "Song (Colin Hennerz Remix)")]
    [InlineData("Song (Live) [Official Video]", "Song (Live)")]
    [InlineData("Mr. Oizo - Being Flat (Official Audio)", "Mr. Oizo - Being Flat")]
    public void Strip_PackagingBesideSongMarkers_KeepsOnlyTheMarkers(
        string title,
        string expected
    ) => Assert.Equal(expected, TitlePackaging.Strip(title));

    [Theory]
    [InlineData("(Official Video)")]
    [InlineData("[Official Audio]")]
    [InlineData("【MV】")]
    [InlineData("(Official Video) [HD]")]
    [InlineData("  (Official Video)  ")]
    public void Strip_TitleThatIsOnlyPackaging_IsKept(string title) =>
        Assert.Equal(title, TitlePackaging.Strip(title));

    [Fact]
    public void Strip_EmptyTitle_StaysEmpty() =>
        Assert.Equal(string.Empty, TitlePackaging.Strip(""));
}

using StemForge.Services;

namespace StemForge.Tests.Services;

public sealed class YtUrlHelperTests
{
    [Theory]
    [InlineData(
        "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
        "https://music.youtube.com/watch?v=dQw4w9WgXcQ"
    )]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData(
        "https://music.youtube.com/watch?v=dQw4w9WgXcQ",
        "https://music.youtube.com/watch?v=dQw4w9WgXcQ"
    )]
    [InlineData(
        "https://m.youtube.com/watch?v=dQw4w9WgXcQ",
        "https://music.youtube.com/watch?v=dQw4w9WgXcQ"
    )]
    [InlineData("dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://soundcloud.com/artist/track", "https://soundcloud.com/artist/track")]
    [InlineData("https://open.spotify.com/track/123", "https://open.spotify.com/track/123")]
    public void TryNormalize_YouTubeVariants_ConvertsToMusicUrl(string input, string expected)
    {
        Assert.True(YtUrlHelper.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file.mp3")]
    public void TryNormalize_InvalidInput_ReturnsFalse(string? input)
    {
        Assert.False(YtUrlHelper.TryNormalize(input, out var normalized));
        Assert.Null(normalized);
    }
}

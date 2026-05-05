using StemForge.Services;

namespace StemForge.Tests.Services;

public sealed class JobQueueServiceTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://soundcloud.com/artist/track", "https://soundcloud.com/artist/track")]
    [InlineData("https://open.spotify.com/track/123", "https://open.spotify.com/track/123")]
    public void NormalizeUrl_YouTubeVariants_ConvertsToMusicUrl(string input, string expected)
    {
        Assert.Equal(expected, JobQueueService.NormalizeUrl(input));
    }
}

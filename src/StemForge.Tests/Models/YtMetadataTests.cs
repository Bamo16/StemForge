using StemForge.Models;

namespace StemForge.Tests.Models;

public sealed class YtMetadataTests
{
    [Fact]
    public void DisplayTitle_ArtistNonEmpty_ReturnsCombinedTitle()
    {
        var meta = Build(title: "Track", artist: "Artist");
        Assert.Equal("Artist - Track", meta.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_ArtistNull_ReturnsJustTitle()
    {
        var meta = Build(title: "Track", artist: null);
        Assert.Equal("Track", meta.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_ArtistWhitespace_ReturnsJustTitle()
    {
        var meta = Build(title: "Track", artist: "   ");
        Assert.Equal("Track", meta.DisplayTitle);
    }

    private static YtMetadata Build(string title, string? artist) =>
        new(
            SourceUrl: "https://example.com",
            Title: title,
            Artist: artist,
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: null,
            MediaUrl: "https://media.example.com/audio"
        );
}

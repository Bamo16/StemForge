namespace StemForge.Tests.Downloading;

public sealed class YtDlpMetadataTests
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

    [Fact]
    public void TryPinFormat_OfferedFormat_RepointsCodecBitrateAndMediaUrl()
    {
        var meta = BuildWithFormats(
            selected: "141",
            new YtDlpFormat
            {
                FormatId = "141",
                AudioCodec = "mp4a.40.2",
                AverageAudioBitrate = 257.5,
                Url = "https://media.example.com/141",
            },
            new YtDlpFormat
            {
                FormatId = "774",
                AudioCodec = "opus",
                AverageAudioBitrate = 257.3,
                Url = "https://media.example.com/774",
            }
        );

        Assert.True(meta.TryPinFormat("774", out var pinned));
        Assert.Equal("774", pinned.FormatId);
        Assert.Equal("opus", pinned.SourceCodec);
        Assert.Equal(257.3, pinned.SourceBitrateKbps);
        Assert.Equal("https://media.example.com/774", pinned.MediaUrl);
    }

    [Fact]
    public void TryPinFormat_FormatNotOffered_ReturnsFalseWithoutFallback()
    {
        // A miss must not quietly leave the auto pick in place: silent substitution is the
        // behaviour pinning exists to prevent.
        var meta = BuildWithFormats(
            selected: "141",
            new YtDlpFormat { FormatId = "141", Url = "https://media.example.com/141" }
        );

        Assert.False(meta.TryPinFormat("999", out var pinned));
        Assert.Null(pinned);
    }

    [Fact]
    public void TryPinFormat_FormatOfferedWithoutDirectUrl_ReturnsFalse()
    {
        // Fragmented formats carry no directly streamable URL, so pinning one cannot be honoured.
        var meta = BuildWithFormats(
            selected: "141",
            new YtDlpFormat { FormatId = "141", Url = "https://media.example.com/141" },
            new YtDlpFormat { FormatId = "774", Url = null }
        );

        Assert.False(meta.TryPinFormat("774", out _));
    }

    [Fact]
    public void TryPinFormat_NoCandidateList_ReturnsFalse()
    {
        var meta = Build(title: "Track", artist: null);
        Assert.False(meta.TryPinFormat("141", out _));
    }

    private static YtDlpMetadata BuildWithFormats(string selected, params YtDlpFormat[] formats) =>
        new(
            SourceUrl: "https://example.com",
            Title: "Track",
            Artist: null,
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: selected,
            MediaUrl: "https://media.example.com/auto",
            AudioFormats: formats,
            Extractor: "youtube"
        );

    private static YtDlpMetadata Build(string title, string? artist) =>
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

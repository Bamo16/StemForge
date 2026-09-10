namespace StemForge.Tests.Downloading;

public sealed class YtDlpMetadataTests
{
    [Fact]
    public void DisplayTitle_ArtistNonEmpty_ReturnsCombinedTitle()
    {
        var meta = Build("Track", "Artist");
        Assert.Equal("Artist - Track", meta.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_ArtistNull_ReturnsJustTitle()
    {
        var meta = Build("Track");
        Assert.Equal("Track", meta.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_ArtistWhitespace_ReturnsJustTitle()
    {
        var meta = Build("Track", "   ");
        Assert.Equal("Track", meta.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_ArtistCreditedAsFeaturedInTitle_OmitsThatArtist()
    {
        var meta = Build("BIRDBRAIN (feat. OK Glass)", "Jamie Paige", "OK Glass", "ash taylor");

        // The filename stops repeating what the title already says...
        Assert.Equal("Jamie Paige, ash taylor - BIRDBRAIN (feat. OK Glass)", meta.DisplayTitle);
        // ...while Artist keeps every credit, because it is what reaches the file's ARTIST tag.
        Assert.Equal("Jamie Paige, OK Glass, ash taylor", meta.Artist);
    }

    [Fact]
    public void BaseName_TitleHasNoInvalidCharacters_MatchesDisplayTitle()
    {
        var meta = Build("Track", "Artist");
        Assert.Equal("Artist - Track", meta.BaseName);
    }

    [Fact]
    public void BaseName_TitleHasInvalidCharacters_RemovesThem()
    {
        // Dropped, not substituted: the downloaded file's name is the one the stem title is later
        // derived from, so this is the only sanitiser that ever sees a raw title.
        var meta = Build("A: B / C?", "Artist");
        // Removed outright rather than replaced, and surrounding whitespace is left alone, so a
        // separator that sat between spaces leaves both behind. Long-standing download behaviour.
        Assert.Equal("Artist - A B  C", meta.BaseName);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), meta.BaseName.Contains);
    }

    [Fact]
    public void BaseName_NoArtist_IsTitleAlone()
    {
        var meta = Build("Track");
        Assert.Equal("Track", meta.BaseName);
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
        var meta = Build("Track");
        Assert.False(meta.TryPinFormat("141", out _));
    }

    private static YtDlpMetadata BuildWithFormats(string selected, params YtDlpFormat[] formats) =>
        new(
            SourceUrl: "https://example.com",
            Title: "Track",
            Artists: null,
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: selected,
            MediaUrl: "https://media.example.com/auto",
            AudioFormats: formats,
            Extractor: "youtube"
        );

    private static YtDlpMetadata Build(string title, params string[] artists) =>
        new(
            SourceUrl: "https://example.com",
            Title: title,
            Artists: artists,
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: null,
            MediaUrl: "https://media.example.com/audio"
        );
}

namespace StemForge.Tests.Downloading;

/// <summary>
/// YouTube's AI auto-dubbing emits one audio format per language, and a dub can out-bitrate the
/// original, so a bitrate-driven choice will sometimes pick one. A dub is the original with its
/// vocal stem separated out and replaced by synthesised speech, so selecting one feeds
/// already-separated audio into a separator.
///
/// The filter has to be relative: yt-dlp scores the original track 10 and dubs -1, but on an
/// undubbed video every format scores -1, so an absolute "keep >= 10" predicate would discard
/// every candidate on every ordinary video. Shapes below are taken from real resolves.
/// </summary>
public sealed class AudioDubFilteringTests
{
    [Fact]
    public void AudioOnlyFormats_UndubbedVideo_KeepsEveryCandidate()
    {
        // Every format scores -1 here. This is the case an absolute threshold would empty out.
        var info = Info(
            Fmt("141", 257.5, 44100),
            Fmt("774", 257.3, 48000),
            Fmt("140", 129.5, 44100),
            Fmt("251", 128.9, 48000),
            Fmt("250", 61.1, 48000),
            Fmt("249", 46.2, 48000)
        );

        Assert.Equal(6, info.AudioOnlyFormats.Count);
        Assert.Equal("141", info.SelectBestAudioFormat().FormatId);
    }

    [Fact]
    public void AudioOnlyFormats_DubbedVideo_KeepsOnlyTheOriginalLanguage()
    {
        var info = DubbedVideo();

        var kept = info.AudioOnlyFormats;
        Assert.All(kept, f => Assert.Equal(10, f.LanguagePreference));
        // The DRC twins go too, since their bare equivalents survived.
        Assert.Equal(["140-9", "251-9"], kept.Select(f => f.FormatId).Order());
    }

    [Fact]
    public void SelectBestAudioFormat_DubbedVideo_DoesNotPickAHigherBitrateDub()
    {
        // The regression this exists for: 251-8 (Japanese) at 145.5 kbps outranks the English
        // original at 129.6 on raw bitrate, and would win without the filter.
        var selected = DubbedVideo().SelectBestAudioFormat();

        Assert.Equal(10, selected.LanguagePreference);
        Assert.Equal("140-9", selected.FormatId);
    }

    [Fact]
    public void SelectBestAudioFormat_PrefersBareVariantOverItsDrcTwin()
    {
        // 140 and 140-drc are the same rung; DRC is compressed dynamics, so it loses the tie.
        var info = Info(
            Fmt("140-drc", 129.6, 44100, drc: true),
            Fmt("140", 129.5, 44100),
            Fmt("251", 128.9, 48000)
        );

        Assert.Equal("140", info.SelectBestAudioFormat().FormatId);
    }

    [Fact]
    public void AudioOnlyFormats_DropsDrcWhenItsBareTwinIsPresent()
    {
        var kept = Info(
            Fmt("140-drc", 129.6, 44100, drc: true),
            Fmt("140", 129.5, 44100)
        ).AudioOnlyFormats;

        Assert.Equal(["140"], kept.Select(f => f.FormatId));
    }

    [Fact]
    public void AudioOnlyFormats_KeepsDrcWhenItIsTheOnlyRouteToThatRung()
    {
        // On some videos the bare itag is absent altogether; dropping DRC there would discard the
        // rung entirely rather than merely preferring the uncompressed copy.
        var kept = Info(
            Fmt("140-drc", 129.6, 44100, drc: true),
            Fmt("251", 128.9, 48000)
        ).AudioOnlyFormats;

        Assert.Equal(["140-drc", "251"], kept.Select(f => f.FormatId).Order());
    }

    [Theory]
    [InlineData("140", "140")]
    [InlineData("140-drc", "140")]
    [InlineData("140-9", "140")]
    [InlineData("251-10", "251")]
    public void BareItag_StripsPerVideoSuffixes(string formatId, string expected) =>
        Assert.Equal(expected, AudioFormatInfo.BareItag(formatId));

    [Theory]
    [InlineData("250-drc")]
    [InlineData("250-9")]
    [InlineData("141-drc")]
    public void IsAuthGated_SuffixedId_StillRecognised(string formatId)
    {
        // On a dubbed video the bare id is absent entirely, so matching raw strings against the
        // gated set would report the source as ungated and produce a false session warning.
        Assert.True(AudioFormatInfo.IsAuthGated(formatId, "youtube"));
    }

    [Fact]
    public void TryPinFormat_BareItagResolvesToTheSuffixedOriginal()
    {
        // Pinning "140" on a dubbed video must not fail just because the bare id does not exist.
        var meta = Meta(DubbedVideo().AudioFormatsByPreference());

        Assert.True(meta.TryPinFormat("140", out var pinned));
        Assert.Equal("140-9", pinned.FormatId);
    }

    [Fact]
    public void TryPinFormat_BareItagPrefersNonDrc()
    {
        var meta = Meta([
            Fmt("140-drc", 129.6, 44100, drc: true, langPref: 10),
            Fmt("140-9", 129.5, 44100, langPref: 10),
        ]);

        Assert.True(meta.TryPinFormat("140", out var pinned));
        Assert.Equal("140-9", pinned.FormatId);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Shape of a real auto-dubbed video: the bare ids are gone, replaced by one suffixed format
    /// per language, and a couple of the dubs out-bitrate the English original.
    /// </summary>
    private static YtDlpVideoInfo DubbedVideo() =>
        Info(
            Fmt("251-8", 145.5, 48000, language: "ja"),
            Fmt("251-7", 143.2, 48000, language: "ml"),
            Fmt("251-6", 140.8, 48000, language: "id"),
            Fmt("140-drc", 129.6, 44100, drc: true, langPref: 10, language: "en-US"),
            Fmt("140-9", 129.6, 44100, langPref: 10, language: "en-US"),
            Fmt("251-drc", 112.0, 48000, drc: true, langPref: 10, language: "en-US"),
            Fmt("251-9", 109.3, 48000, langPref: 10, language: "en-US")
        );

    private static YtDlpVideoInfo Info(params YtDlpFormat[] formats) =>
        new()
        {
            Title = "T",
            Extractor = "youtube",
            Formats = [.. formats],
        };

    private static YtDlpFormat Fmt(
        string id,
        double kbps,
        int asr,
        bool drc = false,
        int langPref = -1,
        string? language = null
    ) =>
        new()
        {
            FormatId = id,
            AverageAudioBitrate = kbps,
            AudioSampleRate = asr,
            AudioCodec = id.StartsWith("14") ? "mp4a.40.2" : "opus",
            VideoCodec = "none",
            Url = $"https://media.example.com/{id}",
            LanguagePreference = langPref,
            Language = language,
            FormatNote = drc ? "medium, DRC" : "medium",
        };

    private static YtDlpMetadata Meta(IReadOnlyList<YtDlpFormat> formats) =>
        new(
            SourceUrl: "https://www.youtube.com/watch?v=x",
            Title: "T",
            Artist: null,
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: formats[0].FormatId,
            MediaUrl: "https://media.example.com/auto",
            AudioFormats: formats,
            Extractor: "youtube"
        );
}

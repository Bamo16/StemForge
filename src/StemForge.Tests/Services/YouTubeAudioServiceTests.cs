using StemForge.Models;
using StemForge.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Services;

public sealed class YouTubeAudioServiceTests
{
    private const string ValidJson = """
        {
          "title": "Test Track",
          "artist": "Test Artist",
          "thumbnail": "https://img.example.com/thumb.jpg",
          "url": "https://media.example.com/fallback",
          "acodec": "opus",
          "abr": 160.0,
          "duration": 240.0,
          "format_id": "251",
          "formats": [
            {
              "format_id": "251",
              "acodec": "opus",
              "vcodec": "none",
              "abr": 160.0,
              "asr": 48000,
              "url": "https://media.example.com/251"
            },
            {
              "format_id": "140",
              "acodec": "mp4a.40.2",
              "vcodec": "none",
              "abr": 128.0,
              "asr": 44100,
              "url": "https://media.example.com/140"
            }
          ]
        }
        """;

    // ── DeserializeVideoInfo ─────────────────────────────────────────────────

    [Fact]
    public void DeserializeVideoInfo_ValidJson_FieldsArePopulated()
    {
        var info = YouTubeAudioService.DeserializeVideoInfo(ValidJson);

        Assert.Equal("Test Track", info.Title);
        Assert.Equal("Test Artist", info.Artist);
        Assert.Equal("https://img.example.com/thumb.jpg", info.Thumbnail);
        Assert.NotNull(info.Formats);
        Assert.Equal(2, info.Formats.Count);
    }

    [Fact]
    public void DeserializeVideoInfo_WithNonJsonPrefixLines_ParsesCorrectly()
    {
        var raw = "INFO:yt-dlp: Downloading metadata\nWARNING: some warning\n" + ValidJson;

        var info = YouTubeAudioService.DeserializeVideoInfo(raw);

        Assert.Equal("Test Track", info.Title);
    }

    [Fact]
    public void DeserializeVideoInfo_EmptyString_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            YouTubeAudioService.DeserializeVideoInfo("")
        );
    }

    [Fact]
    public void DeserializeVideoInfo_NonJsonString_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            YouTubeAudioService.DeserializeVideoInfo("this is definitely not json")
        );
    }

    // ── SelectBestAudioFormat ────────────────────────────────────────────────

    [Fact]
    public void SelectBestAudioFormat_EmptyFormats_ReturnsFallbackWithNullUrl()
    {
        var info = new YtDlpVideoInfo { Formats = [] };
        Assert.Null(info.SelectBestAudioFormat().Url);
    }

    [Fact]
    public void SelectBestAudioFormat_Only441Formats_PicksHighestBitrate()
    {
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                MakeAudioFormat("140", "mp4a.40.2", abr: 128.0, asr: 44100),
                MakeAudioFormat("141", "mp4a.40.2", abr: 256.0, asr: 44100),
            ],
        };

        var result = info.SelectBestAudioFormat();

        Assert.Equal("141", result?.FormatId);
    }

    [Fact]
    public void SelectBestAudioFormat_Only48Formats_PicksHighestBitrate()
    {
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                MakeAudioFormat("250", "opus", abr: 70.0, asr: 48000),
                MakeAudioFormat("251", "opus", abr: 160.0, asr: 48000),
            ],
        };

        var result = info.SelectBestAudioFormat();

        Assert.Equal("251", result?.FormatId);
    }

    [Fact]
    public void SelectBestAudioFormat_441At128_Vs_48At140_Within10Pct_Picks441()
    {
        // 128 >= 140 * 0.90 (= 126) → within 10%, prefer 44.1 kHz
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                MakeAudioFormat("140", "mp4a.40.2", abr: 128.0, asr: 44100),
                MakeAudioFormat("251", "opus", abr: 140.0, asr: 48000),
            ],
        };

        var result = info.SelectBestAudioFormat();

        Assert.Equal("140", result?.FormatId);
    }

    [Fact]
    public void SelectBestAudioFormat_441At128_Vs_48At200_Over10Pct_Picks48()
    {
        // 128 < 200 * 0.90 (= 180) → 48 kHz has >10% advantage
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                MakeAudioFormat("140", "mp4a.40.2", abr: 128.0, asr: 44100),
                MakeAudioFormat("251", "opus", abr: 200.0, asr: 48000),
            ],
        };

        var result = info.SelectBestAudioFormat();

        Assert.Equal("251", result?.FormatId);
    }

    [Fact]
    public void SelectBestAudioFormat_VideoFormatsPresent_IgnoresThem()
    {
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                new YtDlpFormat
                {
                    FormatId = "137",
                    AudioCodec = "none",
                    VideoCodec = "avc1",
                    AverageAudioBitrate = 0,
                    Url = "https://media.example.com/137",
                },
                MakeAudioFormat("140", "mp4a.40.2", abr: 128.0, asr: 44100),
            ],
        };

        var result = info.SelectBestAudioFormat();

        Assert.Equal("140", result?.FormatId);
    }

    [Fact]
    public void SelectBestAudioFormat_AcodecNone_ExcludedFromSelection()
    {
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                new YtDlpFormat
                {
                    FormatId = "139",
                    AudioCodec = "none",
                    VideoCodec = "none",
                    Url = "https://media.example.com/139",
                },
            ],
        };

        Assert.Null(info.SelectBestAudioFormat().Url);
    }

    // ── AudioFormatsByPreference ─────────────────────────────────────────────

    [Fact]
    public void AudioFormatsByPreference_OrdersBestFirstAndFloatsRecommended()
    {
        // Representative yt-dlp output: formats in ascending preference (best last), mixed
        // sample rates, plus a video-only format that must be ignored. The recommended pick is
        // the 44.1 kHz AAC (format 140) — yt-dlp's default would rank the 160 kbps 48 kHz Opus
        // higher by bitrate, so floating the recommended one verifies the default/top selection.
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                MakeAudioFormat("249", "opus", abr: 50.0, asr: 48000),
                new YtDlpFormat
                {
                    FormatId = "137",
                    AudioCodec = "none",
                    VideoCodec = "avc1",
                    Url = "https://media.example.com/137",
                },
                MakeAudioFormat("140", "mp4a.40.2", abr: 128.0, asr: 44100),
                MakeAudioFormat("251", "opus", abr: 160.0, asr: 48000),
            ],
        };

        var ordered = info.AudioFormatsByPreference(recommendedFormatId: "140");

        // Video-only 137 dropped; recommended 140 floated to top; remainder best-first by bitrate.
        Assert.Equal(["140", "251", "249"], ordered.Select(f => f.FormatId));
    }

    [Fact]
    public void AudioFormatsByPreference_EqualBitrate_BreaksTieByCodecThenFormatId()
    {
        var info = new YtDlpVideoInfo
        {
            Formats =
            [
                MakeAudioFormat("a-mp3", "mp4a.40.34", abr: 128.0, asr: 44100),
                MakeAudioFormat("b-opus", "opus", abr: 128.0, asr: 48000),
                MakeAudioFormat("c-aac", "mp4a.40.2", abr: 128.0, asr: 44100),
            ],
        };

        var ordered = info.AudioFormatsByPreference(recommendedFormatId: null);

        // Same bitrate/channels → codec preference: opus > aac > mp3.
        Assert.Equal(["b-opus", "c-aac", "a-mp3"], ordered.Select(f => f.FormatId));
    }

    // ── ResolveAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_HappyPath_ReturnsMappedMetadata()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("yt-dlp", ValidJson);
        var svc = BuildService(fake);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/abc",
            new AppSettings(),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal("Test Track", meta.Title);
        Assert.Equal("Test Artist", meta.Artist);
        Assert.Equal("https://img.example.com/thumb.jpg", meta.ThumbnailUrl);
    }

    [Fact]
    public async Task ResolveAsync_ArtistPresent_DisplayTitleIncludesArtist()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("yt-dlp", ValidJson);
        var svc = BuildService(fake);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/abc",
            new AppSettings(),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal("Test Artist - Test Track", meta.DisplayTitle);
    }

    [Fact]
    public async Task ResolveAsync_NoArtist_DisplayTitleIsJustTitle()
    {
        const string json = """
            {
              "title": "Solo Track",
              "url": "https://media.example.com/audio"
            }
            """;
        var fake = new FakeProcessRunner();
        fake.Setup("yt-dlp", json);
        var svc = BuildService(fake);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/xyz",
            new AppSettings(),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal("Solo Track", meta.DisplayTitle);
    }

    [Fact]
    public async Task ResolveAsync_MultipleAudioFormats_AudioFormatsPopulatedAndOrdered()
    {
        var fake = new FakeProcessRunner();
        fake.Setup("yt-dlp", ValidJson);
        var svc = BuildService(fake);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/abc",
            new AppSettings(),
            ct: TestContext.Current.CancellationToken
        );

        Assert.NotNull(meta.AudioFormats);
        Assert.Equal(2, meta.AudioFormats.Count);
        // Ordered descending by bitrate: 251 (160kbps) first, 140 (128kbps) second
        Assert.Equal("251", meta.AudioFormats[0].FormatId);
        Assert.Equal("140", meta.AudioFormats[1].FormatId);
    }

    [Fact]
    public async Task ResolveAsync_441FormatAvailable_MediaUrlIsFormatUrl()
    {
        const string json = """
            {
              "title": "Track",
              "url": "https://media.example.com/fallback",
              "formats": [
                {
                  "format_id": "140",
                  "acodec": "mp4a.40.2",
                  "vcodec": "none",
                  "abr": 128.0,
                  "asr": 44100,
                  "url": "https://media.example.com/140-44khz"
                }
              ]
            }
            """;
        var fake = new FakeProcessRunner();
        fake.Setup("yt-dlp", json);
        var svc = BuildService(fake);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/abc",
            new AppSettings(),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal("https://media.example.com/140-44khz", meta.MediaUrl);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static YouTubeAudioService BuildService(FakeProcessRunner fake)
    {
        var settings = new AppSettings();
        // Pin yt-dlp's resolved path to the bare CLI name so tests are independent of whether
        // the host machine happens to have a bundled yt-dlp at %LOCALAPPDATA%\StemForge\bin.
        settings.SetToolPathOverride(ToolKind.Ytdlp, "yt-dlp");
        var paths = new AppPaths(settings);
        return new YouTubeAudioService(fake, paths);
    }

    private static YtDlpFormat MakeAudioFormat(string id, string acodec, double abr, int asr) =>
        new()
        {
            FormatId = id,
            AudioCodec = acodec,
            VideoCodec = "none",
            AverageAudioBitrate = abr,
            AudioSampleRate = asr,
            Url = $"https://media.example.com/{id}",
        };
}

namespace StemForge.Tests.Models;

/// <summary>
/// Unit tests for YtDlpVideoInfo.SelectBestThumbnail.
/// Each test targets exactly one branch of the selection policy.
/// </summary>
public sealed class YtDlpThumbnailSelectionTests
{
    // ── No thumbnails / fallback ──────────────────────────────────────────────

    [Fact]
    public void SelectBestThumbnail_EmptyList_ReturnsFallbackThumbnail()
    {
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails = [],
        };

        Assert.Equal("https://img.example.com/fallback.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_AllEntriesMissingDimensions_ReturnsFallbackThumbnail()
    {
        // Thumbnails present but no width/height; should fall through to Thumbnail.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                new YtDlpThumbnail { Url = "https://img.example.com/no-dims.jpg" },
                new YtDlpThumbnail { Url = "https://img.example.com/also-no-dims.jpg" },
            ],
        };

        Assert.Equal("https://img.example.com/fallback.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_NoSquareExists_ReturnsFallbackThumbnail()
    {
        // All sized thumbnails are 16:9; no square available.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/hq720.jpg",
            Thumbnails =
            [
                Square(1280, 720, "https://img.example.com/hq720.jpg"),
                Square(640, 360, "https://img.example.com/sd.jpg"),
            ],
        };

        // These are 16:9 (ratio ~1.78), not square.
        Assert.Equal("https://img.example.com/hq720.jpg", info.SelectBestThumbnail());
    }

    // ── Square under cap ─────────────────────────────────────────────────────

    [Fact]
    public void SelectBestThumbnail_MultipleSquaresUnderCap_PicksLargest()
    {
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(120, 120, "https://img.example.com/120x120.jpg"),
                MakeThumbnail(600, 600, "https://img.example.com/600x600.jpg"),
                MakeThumbnail(1000, 1000, "https://img.example.com/1000x1000.jpg"),
            ],
        };

        // 1000 px is the largest at or below 1200 px.
        Assert.Equal("https://img.example.com/1000x1000.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_SquareExactlyAtCap_IsIncluded()
    {
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(1200, 1200, "https://img.example.com/1200x1200.jpg"),
                MakeThumbnail(800, 800, "https://img.example.com/800x800.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/1200x1200.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_SquareRatioJustInsideBounds_CountsAsSquare()
    {
        // 950x1000 = 0.95 ratio — sits exactly on the lower bound.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails = [MakeThumbnail(950, 1000, "https://img.example.com/almost-square.jpg")],
        };

        Assert.Equal("https://img.example.com/almost-square.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_SquareRatioJustOutsideBounds_NotSquare()
    {
        // 940x1000 = 0.94 — just below the 0.95 floor.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails = [MakeThumbnail(940, 1000, "https://img.example.com/not-square.jpg")],
        };

        Assert.Equal("https://img.example.com/fallback.jpg", info.SelectBestThumbnail());
    }

    // ── All squares over cap ─────────────────────────────────────────────────

    [Fact]
    public void SelectBestThumbnail_AllSquaresOverCap_PicksSmallest()
    {
        // When no square fits under the cap, pick the smallest to minimise unnecessary size.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(2000, 2000, "https://img.example.com/2000x2000.jpg"),
                MakeThumbnail(1400, 1400, "https://img.example.com/1400x1400.jpg"),
                MakeThumbnail(3000, 3000, "https://img.example.com/3000x3000.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/1400x1400.jpg", info.SelectBestThumbnail());
    }

    // ── Tiny square yields to dramatically larger non-square ─────────────────

    [Fact]
    public void SelectBestThumbnail_TinySquareAndLargeWidescreen_PicksWidescreen()
    {
        // Square is 120 px (tiny), non-square is 1280x720 (1280 >= 120 * 3 = 360). Wide wins.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(120, 120, "https://img.example.com/120x120.jpg"),
                MakeThumbnail(1280, 720, "https://img.example.com/1280x720.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/1280x720.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_TinySquareButNonSquareNotDramaticallyLarger_KeepsSquare()
    {
        // Square is 200 px, non-square is 400 px (400 < 200 * 3 = 600). Square wins.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(200, 200, "https://img.example.com/200x200.jpg"),
                MakeThumbnail(400, 225, "https://img.example.com/400x225.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/200x200.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_TinySquareExactlyAtThreshold_StillTiny()
    {
        // Longest side = 299 (below 300 threshold). Non-square is 3x larger: falls through.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(299, 299, "https://img.example.com/299x299.jpg"),
                MakeThumbnail(900, 506, "https://img.example.com/900x506.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/900x506.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_SquareAtThreshold_NotTiny()
    {
        // Longest side = 300 — exactly at threshold, should NOT yield to non-square.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(300, 300, "https://img.example.com/300x300.jpg"),
                MakeThumbnail(1280, 720, "https://img.example.com/1280x720.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/300x300.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_TinySquareNoNonSquareWithDims_KeepsSquare()
    {
        // Only square has dimensions; no non-square to compete with.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(100, 100, "https://img.example.com/100x100.jpg"),
                new YtDlpThumbnail { Url = "https://img.example.com/no-dims.jpg" },
            ],
        };

        Assert.Equal("https://img.example.com/100x100.jpg", info.SelectBestThumbnail());
    }

    // ── Mixed: square among non-squares ──────────────────────────────────────

    [Fact]
    public void SelectBestThumbnail_MixedSquareAndWidescreen_PicksBestSquare()
    {
        // Realistic YouTube Music scenario: 16:9 widescreens + square album art.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                MakeThumbnail(1280, 720, "https://img.example.com/hq720.jpg"),
                MakeThumbnail(640, 360, "https://img.example.com/sd.jpg"),
                MakeThumbnail(226, 226, "https://img.example.com/226x226.jpg"),
                MakeThumbnail(576, 576, "https://img.example.com/576x576.jpg"),
            ],
        };

        // 576x576 is the largest square under 1200 px.
        Assert.Equal("https://img.example.com/576x576.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_MissingDimensionsIgnored_SizedSquareUsed()
    {
        // One entry has no dimensions (must be ignored); one square with dimensions present.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails =
            [
                new YtDlpThumbnail { Url = "https://img.example.com/no-dims.jpg" },
                MakeThumbnail(500, 500, "https://img.example.com/500x500.jpg"),
            ],
        };

        Assert.Equal("https://img.example.com/500x500.jpg", info.SelectBestThumbnail());
    }

    // ── Single-thumbnail sources (regression) ────────────────────────────────

    [Fact]
    public void SelectBestThumbnail_SingleThumbnailNoSquares_ReturnsFallback()
    {
        // Simulates a source that only exposes yt-dlp's single top-level thumbnail.
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/single.jpg",
            Thumbnails = [MakeThumbnail(1280, 720, "https://img.example.com/1280x720.jpg")],
        };

        Assert.Equal("https://img.example.com/single.jpg", info.SelectBestThumbnail());
    }

    [Fact]
    public void SelectBestThumbnail_SingleSquareThumbnail_ReturnsThatSquare()
    {
        var info = new YtDlpVideoInfo
        {
            Thumbnail = "https://img.example.com/fallback.jpg",
            Thumbnails = [MakeThumbnail(500, 500, "https://img.example.com/500x500.jpg")],
        };

        Assert.Equal("https://img.example.com/500x500.jpg", info.SelectBestThumbnail());
    }

    // ── JSON round-trip: thumbnails array parsed via source-gen context ───────

    [Fact]
    public void DeserializeVideoInfo_WithThumbnailsArray_ParsedCorrectly()
    {
        const string json = """
            {
              "title": "Track",
              "url": "https://media.example.com/audio",
              "thumbnail": "https://img.example.com/fallback.jpg",
              "thumbnails": [
                { "id": "0", "url": "https://img.example.com/120.jpg", "preference": -13, "width": 120, "height": 90 },
                { "id": "1", "url": "https://img.example.com/640.jpg", "width": 640, "height": 480 },
                { "id": "sq", "url": "https://img.example.com/500sq.jpg", "width": 500, "height": 500, "resolution": "500x500" },
                { "id": "noDims", "url": "https://img.example.com/nodims.jpg" }
              ]
            }
            """;

        var info = StemForge.Core.Downloading.YouTubeAudioService.DeserializeVideoInfo(json);

        Assert.Equal(4, info.Thumbnails.Count);

        // Entry with all fields.
        Assert.Equal("0", info.Thumbnails[0].Id);
        Assert.Equal("https://img.example.com/120.jpg", info.Thumbnails[0].Url);
        Assert.Equal(-13, info.Thumbnails[0].Preference);
        Assert.Equal(120, info.Thumbnails[0].Width);
        Assert.Equal(90, info.Thumbnails[0].Height);

        // Entry with resolution string.
        Assert.Equal("500x500", info.Thumbnails[2].Resolution);

        // Entry missing width/height should deserialize with null dimensions.
        Assert.Null(info.Thumbnails[3].Width);
        Assert.Null(info.Thumbnails[3].Height);
    }

    [Fact]
    public void DeserializeVideoInfo_ThumbnailsArrayAbsent_EmptyList()
    {
        const string json = """
            {
              "title": "Track",
              "url": "https://media.example.com/audio",
              "thumbnail": "https://img.example.com/fallback.jpg"
            }
            """;

        var info = StemForge.Core.Downloading.YouTubeAudioService.DeserializeVideoInfo(json);

        Assert.Empty(info.Thumbnails);
        // SelectBestThumbnail falls back to Thumbnail field.
        Assert.Equal("https://img.example.com/fallback.jpg", info.SelectBestThumbnail());
    }

    // ── ResolveAsync: thumbnail selection flows through to ThumbnailUrl ───────

    [Fact]
    public async Task ResolveAsync_SquareThumbnailPresent_ThumbnailUrlIsSquare()
    {
        const string json = """
            {
              "title": "Track",
              "url": "https://media.example.com/audio",
              "thumbnail": "https://img.example.com/hq720.jpg",
              "thumbnails": [
                { "id": "wide", "url": "https://img.example.com/hq720.jpg", "width": 1280, "height": 720 },
                { "id": "sq",   "url": "https://img.example.com/600sq.jpg", "width": 600,  "height": 600 }
              ],
              "formats": [
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

        var fake = new StemForge.Tests.Fakes.FakeProcessRunner();
        fake.Setup("yt-dlp", json);
        var settings = new StemForge.Core.AppSettings();
        settings.SetToolPathOverride(StemForge.Core.Tooling.ToolKind.Ytdlp, "yt-dlp");
        var paths = new StemForge.Core.AppPaths(settings);
        var svc = new StemForge.Core.Downloading.YouTubeAudioService(fake, paths);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/test",
            settings,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal("https://img.example.com/600sq.jpg", meta.ThumbnailUrl);
    }

    [Fact]
    public async Task ResolveAsync_NoSquareThumbnails_ThumbnailUrlIsFallback()
    {
        const string json = """
            {
              "title": "Track",
              "url": "https://media.example.com/audio",
              "thumbnail": "https://img.example.com/hq720.jpg",
              "thumbnails": [
                { "id": "wide", "url": "https://img.example.com/hq720.jpg", "width": 1280, "height": 720 }
              ],
              "formats": [
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

        var fake = new StemForge.Tests.Fakes.FakeProcessRunner();
        fake.Setup("yt-dlp", json);
        var settings = new StemForge.Core.AppSettings();
        settings.SetToolPathOverride(StemForge.Core.Tooling.ToolKind.Ytdlp, "yt-dlp");
        var paths = new StemForge.Core.AppPaths(settings);
        var svc = new StemForge.Core.Downloading.YouTubeAudioService(fake, paths);

        var meta = await svc.ResolveAsync(
            "https://youtu.be/test",
            settings,
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal("https://img.example.com/hq720.jpg", meta.ThumbnailUrl);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static YtDlpThumbnail MakeThumbnail(int width, int height, string url) =>
        new()
        {
            Width = width,
            Height = height,
            Url = url,
        };

    /// <summary>
    /// "Square" overload name is intentionally aliased to make tests readable when the thumbnail
    /// is 16:9 and the helper is called for that purpose. Use MakeThumbnail for those calls.
    /// </summary>
    private static YtDlpThumbnail Square(int width, int height, string url) =>
        MakeThumbnail(width, height, url);
}

using System.Globalization;
using System.Text.Json;

namespace StemForge.Core.Downloading;

/// <summary>
/// Downloads a URL's audio in three steps: yt-dlp resolves the metadata and picks a format, yt-dlp
/// downloads that format to a temp file, and ffmpeg converts it to the output format at 44.1 kHz.
/// </summary>
public sealed class YouTubeAudioService(IProcessRunner runner, AppPaths paths, YtdlpUpdater updater)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;
    private readonly YtdlpUpdater _updater = updater;

    public async Task<YtDlpMetadata> ResolveAsync(
        string url,
        AppSettings settings,
        IProgress<string>? log = null,
        CancellationToken ct = default
    )
    {
        // A stale yt-dlp gets media URLs YouTube refuses (#95), so bring it current first.
        await _updater.EnsureCurrentAsync(ct);

        var args = new List<string>
        {
            "--dump-single-json",
            "--no-playlist",
            "--format",
            "bestaudio/best",
            // YouTube rotates JS-based "n-challenges" that require an up-to-date solver script.
            // ejs:github fetches that script from the yt-dlp/yt-dlp-ejs repo (cached locally by
            // yt-dlp; not a GitHub round-trip on every call). Without it, format extraction
            // silently returns only image thumbnails ("Requested format is not available").
            // This is intentionally unconditional: we cannot reliably determine at call-time
            // whether a usable JS runtime is present (the user may have deno on PATH or a
            // bare-name settings override rather than StemForge's bundled binary).
            "--remote-components",
            "ejs:github",
        };

        // Point yt-dlp at StemForge's deno when it exists; a bare-name override leaves yt-dlp to
        // find deno on PATH itself. ArgumentList handles OS-level quoting; no embedded quotes needed.
        if (File.Exists(_paths.Deno))
            args.AddRange(["--js-runtimes", $"deno:{_paths.Deno}"]);
        args.Add(url);

        // Stderr streams live (yt-dlp info lines); stdout (the JSON blob) is captured silently.
        ProcessRunner.Result result;
        if (CookieArgs(settings.YtdlpCookiesFromBrowser) is not { } cookieArgs)
            result = await _runner.RunStreamingStderrAsync(_paths.Ytdlp, args, log, ct: ct);
        else
        {
            try
            {
                result = await _runner.RunStreamingStderrAsync(
                    _paths.Ytdlp,
                    [.. cookieArgs, .. args],
                    log,
                    ct: ct
                );
            }
            catch (ProcessRunner.ProcessFailedException ex)
                when (ex.Stderr.Contains("cookie", StringComparison.OrdinalIgnoreCase))
            {
                // Unreadable cookies (Chrome locks its database and, on Windows, encrypts it so
                // only Chrome can read it) shouldn't cost the download; premium formats are what
                // they bought, and the premium check reports their absence.
                var message =
                    $"Could not read cookies from \"{settings.YtdlpCookiesFromBrowser}\"; "
                    + "continuing without them, so premium formats are unavailable.";
                AppLogger.Warning("yt-dlp", message);
                log?.Report($"WARNING: {message}");

                result = await _runner.RunStreamingStderrAsync(_paths.Ytdlp, args, log, ct: ct);
            }
        }

        var info = DeserializeVideoInfo(result.Stdout);

        // Pick the best audio format — prefer 44.1 kHz to avoid resampling loss (audio-separator
        // normalises everything to 44.1 kHz internally). Fall back to yt-dlp's top-level url.
        if (info.SelectBestAudioFormat() is not { Url: { } mediaUrl } selected)
        {
            throw new InvalidOperationException(
                "yt-dlp metadata missing direct media URL; check format selector."
            );
        }

        LogAudioFormats(info.AudioOnlyFormats, selected.FormatId);

        return new YtDlpMetadata(
            // Prefer yt-dlp's canonical page URL so the provenance tag is stable and free of
            // tracking params. Fall back to the originally requested URL if it is absent.
            SourceUrl: info.WebpageUrl ?? info.OriginalUrl ?? url,
            Title: info.Title,
            // Collapse YouTube Music's per-role repeats before anything downstream sees them:
            // this value reaches the filename, the JSON row and the file's own ARTIST tag.
            Artists: ArtistNames.Canonical(info.Artists, info.Artist),
            Uploader: info.Uploader,
            SourceCodec: selected.AudioCodec,
            SourceBitrateKbps: selected.AudioBitrate,
            DurationSeconds: info.Duration,
            FormatId: selected.FormatId,
            MediaUrl: mediaUrl,
            ThumbnailUrl: info.SelectBestThumbnail(),
            // Ordered best-first by bitrate; the AUTO pick (selected) is tagged in place by the
            // picker rather than floated to the top.
            AudioFormats: info.AudioFormatsByPreference() is { Count: > 0 } ranked
                ? ranked
                : [selected],
            Extractor: info.Extractor,
            InfoJson: result.Stdout
        );
    }

    /// <summary>yt-dlp's cookie arguments for a browser name or a cookies.txt path, or null for none.</summary>
    private static string[]? CookieArgs(string? cookies)
    {
        if (string.IsNullOrWhiteSpace(cookies))
            return null;

        // A path, or a name ending .txt, is an exported cookies file; anything else is a browser.
        var isFile =
            cookies.Contains(Path.DirectorySeparatorChar)
            || cookies.Contains(Path.AltDirectorySeparatorChar)
            || cookies.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

        return [isFile ? "--cookies" : "--cookies-from-browser", cookies];
    }

    /// Resolves metadata for a URL and returns it, or null if the URL is invalid or yt-dlp fails.
    /// Used for format preview — never throws.
    public async Task<YtDlpMetadata?> GetAudioFormatInfoAsync(
        string url,
        AppSettings settings,
        CancellationToken ct = default
    )
    {
        try
        {
            return await ResolveAsync(url, settings, log: null, ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Debug("yt-dlp", $"Format preview failed: {ex.Message}");
            return null;
        }
    }

    public async Task<string> DownloadAsync(
        YtDlpMetadata meta,
        AudioFormat format,
        string outDir,
        IProgress<string>? log,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(outDir);
        var outputPath = Path.Combine(outDir, $"{meta.BaseName}.{FfmpegArgs.Extension(format)}");

        var work = Path.Combine(Path.GetTempPath(), $"stemforge-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var source = await FetchSourceAsync(meta, work, log, ct);

            List<string> args =
            [
                .. FfmpegArgs.Baseline,
                "-i",
                source,
                // Normalise to 44.1 kHz: audio-separator uses this internally, so keeping 48 kHz
                // sources at their native rate only introduces a resampling step at separation time.
                "-ar",
                "44100",
                .. FfmpegArgs.Codec(format),
                outputPath,
            ];
            await _runner.RunStreamingAsync(_paths.Ffmpeg, args, log, ct: ct);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Debug("yt-dlp", $"Could not remove {work}: {ex.Message}");
            }
        }

        return outputPath;
    }

    /// <summary>
    /// Has yt-dlp download the chosen format into <paramref name="work"/> and returns its path.
    /// ffmpeg used to fetch the media URL itself, but YouTube refuses a client that skips what
    /// yt-dlp does for each request, sometimes partway through a long download (#95).
    /// </summary>
    private async Task<string> FetchSourceAsync(
        YtDlpMetadata meta,
        string work,
        IProgress<string>? log,
        CancellationToken ct
    )
    {
        List<string> args =
        [
            "--format",
            meta.FormatId ?? "bestaudio/best",
            "--output",
            Path.Combine(work, "source.%(ext)s"),
            // yt-dlp repairs some containers with ffmpeg after downloading.
            "--ffmpeg-location",
            _paths.Ffmpeg,
            // Prints the finished file's path on stdout, and implies --quiet, so stdout is only that.
            "--print",
            "after_move:filepath",
            "--no-playlist",
        ];

        // Reuse the resolve's own JSON rather than resolving again: it carries the formats, the
        // headers and any cookies, so the download needs no second extraction or challenge.
        if (meta.InfoJson is { } infoJson)
        {
            var infoPath = Path.Combine(work, "info.json");
            await File.WriteAllTextAsync(infoPath, infoJson, ct);
            args.AddRange(["--load-info-json", infoPath]);
        }
        else
        {
            args.Add(meta.SourceUrl);
        }

        var result = await _runner.RunStreamingStderrAsync(_paths.Ytdlp, args, log, ct: ct);

        return result.Stdout.Trim() is { Length: > 0 } path && File.Exists(path)
            ? path
            : throw new InvalidOperationException(
                $"yt-dlp finished without reporting the file it downloaded for {meta.SourceUrl}."
            );
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private static void LogAudioFormats(List<YtDlpFormat> formats, string? selectedFormatId)
    {
        if (formats.OrderByDescending(f => f.AudioBitrate).ToList() is not { Count: > 0 } rows)
            return;

        AppLogger.Info("yt-dlp", $"  {"ID", -14} {"Codec", -8} {"kbps", 6} {"kHz", 6}  Note");
        foreach (var f in rows)
        {
            var marker = f.FormatId == selectedFormatId ? ">" : " ";
            var kbps = f.AudioBitrate.ToString("F0", CultureInfo.InvariantCulture);
            var khz = f.AudioSampleRate is { } hz
                ? (hz / 1000.0).ToString("F1", CultureInfo.InvariantCulture)
                : "?";
            AppLogger.Info(
                "yt-dlp",
                $"{marker} {f.FormatId, -14} {f.AudioCodec, -8} {kbps, 6} {khz, 6}  {f.FormatNote ?? ""}"
            );
        }
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    internal static YtDlpVideoInfo DeserializeVideoInfo(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("yt-dlp returned no metadata.");

        // Strip any stray non-JSON lines (warnings, stderr bleed, etc.).
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("yt-dlp metadata was not valid JSON.");

        return JsonSerializer.Deserialize(
                raw[start..(end + 1)],
                YtDlpJsonContext.Default.YtDlpVideoInfo
            )
            ?? throw new InvalidOperationException(
                "yt-dlp metadata deserialization returned null."
            );
    }
}

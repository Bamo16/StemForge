using System.Globalization;
using System.Text.Json;
using StemForge.Helpers;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Two-stage YouTube audio download:
///   1. yt-dlp --dump-single-json resolves metadata + selects best audio format URL
///   2. ffmpeg streams that URL to disk in the chosen format with provenance tags
/// No temp file, no double-encoding, full visibility into yt-dlp and ffmpeg progress.
/// </summary>
public sealed class YouTubeAudioService(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;

    private static readonly HashSet<char> _invalidFileNameChars =
    [
        .. Path.GetInvalidFileNameChars(),
    ];

    public async Task<YtDlpMetadata> ResolveAsync(
        string url,
        AppSettings settings,
        IProgress<string>? log = null,
        CancellationToken ct = default
    )
    {
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

        var cookies = settings.YtdlpCookiesFromBrowser;
        if (!string.IsNullOrWhiteSpace(cookies))
        {
            var flag =
                cookies.Contains(Path.DirectorySeparatorChar)
                || cookies.Contains(Path.AltDirectorySeparatorChar)
                || cookies.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? "--cookies"
                    : "--cookies-from-browser";
            args.AddRange([flag, cookies]);
        }

        // Pass bundled deno explicitly so yt-dlp finds it without deno on PATH.
        // ArgumentList handles OS-level quoting automatically; no embedded quotes needed.
        if (Path.IsPathRooted(_paths.Deno))
            args.AddRange(["--js-runtimes", $"deno:{_paths.Deno}"]);
        args.Add(url);

        // Stderr streams live (yt-dlp info lines); stdout (the JSON blob) is captured silently.
        var result = await _runner.RunStreamingStderrAsync(_paths.Ytdlp, args, log, ct);

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
            Artist: info.Artist,
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
            Extractor: info.Extractor
        );
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
        var fileName = $"{SanitizeFileName(meta.DisplayTitle)}.{FfmpegArgs.Extension(format)}";
        var outputPath = Path.Combine(outDir, fileName);

        var args = new List<string>();
        args.AddRange(FfmpegArgs.Baseline);
        args.AddRange(["-i", meta.MediaUrl]);
        // Normalise to 44.1 kHz: audio-separator uses this internally, so keeping 48 kHz
        // sources at their native rate only introduces a resampling step at separation time.
        args.AddRange(["-ar", "44100"]);
        args.AddRange(FfmpegArgs.Codec(format));
        args.Add(outputPath);

        await _runner.RunStreamingAsync(_paths.Ffmpeg, args, log, ct);

        return outputPath;
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

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Where(c => !_invalidFileNameChars.Contains(c)));
}

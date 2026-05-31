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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HttpClient _http = new();

    public async Task<YtMetadata> ResolveAsync(
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
            // YouTube now rotates JS-based "n challenges" that yt-dlp needs an external solver
            // script for. Authorising the upstream EJS repo lets yt-dlp fetch the solver on
            // demand; without this flag, format extraction silently returns only image
            // thumbnails and we get "Requested format is not available". yt-dlp itself
            // still needs a JS runtime on PATH (deno/node/bun) to execute the solver.
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
        // Wrap in double-quotes: yt-dlp's own value parser splits on spaces, so the
        // quotes are embedded in the value string (not just OS-level arg quoting).
        var denoPath = _paths.Deno;
        if (Path.IsPathRooted(denoPath))
            args.AddRange(["--js-runtimes", $"\"deno:{denoPath}\""]);
        args.Add(url);

        // Stderr streams live (yt-dlp info lines); stdout (the JSON blob) is captured silently.
        var result = await _runner.RunStreamingStderrAsync(_paths.Ytdlp, args, log, ct);

        var info = DeserializeVideoInfo(result.Stdout);

        // Pick the best audio format — prefer 44.1 kHz to avoid resampling loss (audio-separator
        // normalises everything to 44.1 kHz internally). Fall back to yt-dlp's top-level url.
        var selected = SelectBestAudioFormat(info);
        var mediaUrl =
            selected?.Url
            ?? info.Url
            ?? throw new InvalidOperationException(
                "yt-dlp metadata missing direct media URL; check format selector."
            );

        var codec = selected?.Acodec ?? info.Acodec;
        var bitrate = selected?.Abr ?? selected?.Tbr ?? info.Abr;
        var asr = selected?.Asr ?? info.Asr;

        IReadOnlyList<YtDlpFormat>? audioFormats = null;
        if (info is { Formats: { Count: > 0 } formats })
        {
            audioFormats =
            [
                .. formats.Where(IsAudioOnly).OrderByDescending(f => f.Abr ?? f.Tbr ?? 0),
            ];
            LogAudioFormats(formats, selected);
        }

        var summary =
            $"resolved: {info.DisplayTitle()}"
            + (codec is not null ? $" · {codec}" : string.Empty)
            + (
                bitrate is { } kbps
                    ? $" @ {kbps.ToString("F0", CultureInfo.InvariantCulture)}k"
                    : string.Empty
            )
            + (
                asr is { } hz
                    ? $" · {(hz / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}kHz"
                    : string.Empty
            )
            + (
                info.Duration is { } dur
                    ? $" · {dur.ToString("F0", CultureInfo.InvariantCulture)}s"
                    : string.Empty
            );
        AppLogger.Info("yt-dlp", summary);

        return new YtMetadata(
            SourceUrl: url,
            Title: info.Title,
            Artist: info.Artist,
            Uploader: info.Uploader,
            SourceCodec: codec,
            SourceBitrateKbps: bitrate,
            DurationSeconds: info.Duration,
            FormatId: selected?.FormatId ?? info.FormatId,
            MediaUrl: mediaUrl,
            ThumbnailUrl: info.Thumbnail,
            AudioFormats: audioFormats,
            Extractor: info.Extractor
        );
    }

    /// Resolves metadata for a URL and returns it, or null if the URL is invalid or yt-dlp fails.
    /// Used for format preview — never throws.
    public async Task<YtMetadata?> GetAudioFormatInfoAsync(
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
        YtMetadata meta,
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

    /// <summary>
    /// Downloads the thumbnail image at <paramref name="url"/> into <paramref name="outDir"/>
    /// and returns the local path. Returns null on failure (non-fatal).
    /// </summary>
    public static async Task<string?> DownloadThumbnailAsync(
        string? url,
        string outDir,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        try
        {
            // Derive extension from URL path; default to .jpg which ffmpeg handles universally.
            var uriPath = new Uri(url).LocalPath;
            var ext = Path.GetExtension(uriPath);
            if (string.IsNullOrEmpty(ext))
                ext = ".jpg";

            var dest = Path.Combine(outDir, $"thumbnail{ext}");
            var bytes = await _http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(dest, bytes, ct);
            return dest;
        }
        catch (Exception ex)
        {
            AppLogger.Debug("yt-dlp", $"Thumbnail download failed: {ex.Message}");
            return null;
        }
    }

    // ── Format selection ──────────────────────────────────────────────────────

    private static bool IsAudioOnly(YtDlpFormat f) =>
        f.Url is not null
        && !string.IsNullOrEmpty(f.Acodec)
        && f.Acodec != "none"
        && (string.IsNullOrEmpty(f.Vcodec) || f.Vcodec == "none");

    /// <summary>
    /// Selects the best audio-only format from the formats list.
    /// Prefers 44.1 kHz unless the best 48 kHz option has more than 10% higher bitrate
    /// (audio-separator always resamples to 44.1 kHz, so starting at 48 kHz just adds
    /// a lossy resampling step with no quality benefit).
    /// </summary>
    internal static YtDlpFormat? SelectBestAudioFormat(YtDlpVideoInfo info)
    {
        var formats = info.Formats;
        if (formats is null or { Count: 0 })
            return null;

        static double Bitrate(YtDlpFormat f) => f.Abr ?? f.Tbr ?? 0;

        var audioOnly = formats.Where(IsAudioOnly).ToList();
        if (audioOnly.Count == 0)
            return null;

        var best441 = audioOnly
            .Where(f => f.Asr is >= 44099 and <= 44101)
            .OrderByDescending(Bitrate)
            .FirstOrDefault();

        var best48 = audioOnly
            .Where(f => f.Asr is >= 47999 and <= 48001)
            .OrderByDescending(Bitrate)
            .FirstOrDefault();

        if (best441 is not null)
        {
            var br48 = best48 is not null ? Bitrate(best48) : 0;
            // Prefer 44.1 kHz if it's within 10% of the best 48 kHz option.
            if (br48 == 0 || Bitrate(best441) >= br48 * 0.90)
                return best441;
        }

        return audioOnly.OrderByDescending(Bitrate).FirstOrDefault();
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private static void LogAudioFormats(List<YtDlpFormat> formats, YtDlpFormat? selected)
    {
        var rows = formats.Where(IsAudioOnly).OrderByDescending(f => f.Abr ?? f.Tbr ?? 0).ToList();

        if (rows.Count == 0)
            return;

        AppLogger.Info("yt-dlp", $"  {"ID", -14} {"Codec", -8} {"kbps", 6} {"kHz", 6}  Note");
        foreach (var f in rows)
        {
            var marker = f.FormatId == selected?.FormatId ? ">" : " ";
            var kbps = (f.Abr ?? f.Tbr ?? 0).ToString("F0", CultureInfo.InvariantCulture);
            var khz = f.Asr is { } hz
                ? (hz / 1000.0).ToString("F1", CultureInfo.InvariantCulture)
                : "?";
            AppLogger.Info(
                "yt-dlp",
                $"{marker} {f.FormatId, -14} {f.Acodec, -8} {kbps, 6} {khz, 6}  {f.FormatNote ?? ""}"
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

        return JsonSerializer.Deserialize<YtDlpVideoInfo>(raw[start..(end + 1)], _jsonOptions)
            ?? throw new InvalidOperationException(
                "yt-dlp metadata deserialization returned null."
            );
    }

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Where(c => !_invalidFileNameChars.Contains(c)));
}

file static class YtDlpVideoInfoExtensions
{
    internal static string DisplayTitle(this YtDlpVideoInfo info) =>
        string.IsNullOrWhiteSpace(info.Artist) ? info.Title : $"{info.Artist} - {info.Title}";
}

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Two-stage YouTube audio download:
///   1. yt-dlp --dump-single-json resolves metadata + a direct media URL (stderr streamed live)
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

    public sealed record YtMetadata(
        string SourceUrl,
        string Title,
        string? Uploader,
        string? SourceCodec,
        double? SourceBitrateKbps,
        double? DurationSeconds,
        string? FormatId,
        string MediaUrl
    );

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

        var jsRuntime = settings.YtdlpJsRuntime;
        if (!string.IsNullOrWhiteSpace(jsRuntime))
            args.AddRange(["--js-runtime", jsRuntime]);

        args.Add(url);

        // Stderr streams live (yt-dlp info lines); stdout (the JSON blob) is captured silently.
        var result = await _runner.RunStreamingStderrAsync(_paths.Ytdlp, args, log, ct);

        var info = DeserializeVideoInfo(result.Stdout);
        var mediaUrl =
            info.Url
            ?? throw new InvalidOperationException(
                "yt-dlp metadata missing direct media URL; check format selector."
            );

        var summary =
            $"resolved: {info.Title}"
            + (info.Acodec is not null ? $" · {info.Acodec}" : string.Empty)
            + (
                info.Abr is { } kbps
                    ? $" @ {kbps.ToString("F0", CultureInfo.InvariantCulture)}k"
                    : string.Empty
            )
            + (
                info.Duration is { } dur
                    ? $" · {dur.ToString("F0", CultureInfo.InvariantCulture)}s"
                    : string.Empty
            );
        AppLogger.Info("yt-dlp", summary);

        return new YtMetadata(
            url,
            info.Title,
            info.Uploader,
            info.Acodec,
            info.Abr,
            info.Duration,
            info.FormatId,
            mediaUrl
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
        var fileName = $"{SanitizeFileName(meta.Title)}.{FfmpegArgs.Extension(format)}";
        var outputPath = Path.Combine(outDir, fileName);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
        var tags = new List<(string Key, string Value)>
        {
            ("source_url", meta.SourceUrl),
            ("source_codec", meta.SourceCodec ?? string.Empty),
            (
                "source_bitrate",
                meta.SourceBitrateKbps?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty
            ),
            ("source_format_id", meta.FormatId ?? string.Empty),
            ("download_date", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            ("tool", $"stemforge/{version}"),
        };

        var args = new List<string>();
        args.AddRange(FfmpegArgs.Baseline);
        args.AddRange(["-i", meta.MediaUrl]);
        args.AddRange(FfmpegArgs.Metadata(tags));
        args.AddRange(FfmpegArgs.Codec(format));
        args.Add(outputPath);

        await _runner.RunStreamingAsync(_paths.Ffmpeg, args, log, ct);

        return outputPath;
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

    /*

    Todo: refine this, extend it to support our YouTube video ID regex stuff and give an out var with the Uri

    private static bool IsValidYtDlpUrl(string inputUrl)
    {
        // 1. Null or empty check
        if (string.IsNullOrWhiteSpace(inputUrl))
            return false;

        // 2. Try to parse the URL
        if (Uri.TryCreate(inputUrl, UriKind.Absolute, out Uri parsedUri))
        {
            // 3. Strictly enforce HTTP or HTTPS.
            // This blocks file://, ftp://, javascript:, etc.
            return parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps;
        }

        // If you want to support raw YouTube IDs (11 chars, alphanumeric/dash/underscore),
        // you could add a fallback Regex check *only* for that specific format here.
        // Otherwise, fail fast.
        return false;
    }

    */

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Where(c => !_invalidFileNameChars.Contains(c)));
}

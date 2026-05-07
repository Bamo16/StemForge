using System.Globalization;
using System.Reflection;
using System.Text.Json;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Two-stage YouTube audio download:
///   1. yt-dlp --dump-single-json resolves metadata + a direct media URL
///   2. ffmpeg streams that URL to disk in the chosen format with provenance tags
/// No temp file, no double-encoding, full visibility into ffmpeg args.
/// </summary>
public sealed class YouTubeAudioService(IProcessRunner runner)
{
    private readonly IProcessRunner _runner = runner;

    private static readonly HashSet<char> _invalidFileNameChars =
    [
        .. Path.GetInvalidFileNameChars(),
    ];

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
        CancellationToken ct
    )
    {
        var args = new List<string>
        {
            "--dump-single-json",
            "--no-playlist",
            "--no-warnings",
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

        // Capture stdout silently — we don't want the full JSON in the log file.
        var ytdlpExe = string.IsNullOrWhiteSpace(settings.YtdlpPath) ? "yt-dlp" : settings.YtdlpPath;
        var result = await _runner.RunCheckedAsync(ytdlpExe, args, ct, logRawLines: false);

        var meta = ParseJson(url, result.Stdout);
        var summary =
            $"resolved: {meta.Title}"
            + (meta.SourceCodec is not null ? $" · {meta.SourceCodec}" : string.Empty)
            + (meta.SourceBitrateKbps is { } kbps
                ? $" @ {kbps.ToString("F0", CultureInfo.InvariantCulture)}k"
                : string.Empty)
            + (meta.DurationSeconds is { } dur
                ? $" · {dur.ToString("F0", CultureInfo.InvariantCulture)}s"
                : string.Empty);
        AppLogger.Info("yt-dlp", summary);

        return meta;
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
            ("source_bitrate", meta.SourceBitrateKbps?.ToString("F1", CultureInfo.InvariantCulture)
                ?? string.Empty),
            ("source_format_id", meta.FormatId ?? string.Empty),
            ("download_date", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            ("tool", $"stemforge/{version}"),
        };

        var args = new List<string>();
        args.AddRange(FfmpegArgs.Baseline());
        args.AddRange(["-i", meta.MediaUrl]);
        args.AddRange(FfmpegArgs.Metadata(tags));
        args.AddRange(FfmpegArgs.Codec(format));
        args.Add(outputPath);

        await _runner.RunStreamingAsync("ffmpeg", args, log, ct);

        return outputPath;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    internal static YtMetadata ParseJson(string sourceUrl, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("yt-dlp returned no metadata.");

        // Strip any stray prefix/suffix lines around the JSON object.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("yt-dlp metadata was not valid JSON.");

        using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
        var root = doc.RootElement;

        var title = GetString(root, "title") ?? "Unknown";
        var uploader = GetString(root, "uploader");
        var acodec = GetString(root, "acodec");
        var abr = GetDouble(root, "abr");
        var duration = GetDouble(root, "duration");
        var formatId = GetString(root, "format_id");
        var mediaUrl =
            GetString(root, "url")
            ?? throw new InvalidOperationException(
                "yt-dlp metadata missing direct media URL; check format selector."
            );

        return new YtMetadata(
            sourceUrl,
            title,
            uploader,
            acodec,
            abr,
            duration,
            formatId,
            mediaUrl
        );
    }

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static double? GetDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : null;

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Where(c => !_invalidFileNameChars.Contains(c)));
}

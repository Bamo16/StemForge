using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Builds ffmpeg argument lists with consistent baseline flags so we don't drift
/// across call sites. Always seeds: -hide_banner -nostats -loglevel warning -y.
/// </summary>
public static class FfmpegArgs
{
    public static IEnumerable<string> Baseline()
    {
        yield return "-hide_banner";
        yield return "-nostats";
        yield return "-loglevel";
        yield return "warning";
        yield return "-y";
    }

    /// <summary>Codec + format-specific flags for the chosen output format.</summary>
    public static IEnumerable<string> Codec(AudioFormat format) =>
        format switch
        {
            AudioFormat.Flac => ["-c:a", "flac", "-compression_level", "8"],
            AudioFormat.Wav => ["-c:a", "pcm_s24le"],
            AudioFormat.Mp3 => ["-c:a", "libmp3lame", "-b:a", "320k"],
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    public static string Extension(AudioFormat format) =>
        format switch
        {
            AudioFormat.Flac => "flac",
            AudioFormat.Wav => "wav",
            AudioFormat.Mp3 => "mp3",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    /// <summary>
    /// Pairs of (key, value) to embed as ffmpeg `-metadata key=value` flags.
    /// Each pair becomes two argv entries: ["-metadata", "key=value"].
    /// </summary>
    public static IEnumerable<string> Metadata(IEnumerable<(string Key, string Value)> tags)
    {
        foreach (var (key, value) in tags)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            yield return "-metadata";
            yield return $"{key}={value}";
        }
    }
}

using StemForge.Core.Models;

namespace StemForge.Core.Helpers;

/// <summary>
/// Builds ffmpeg argument lists with consistent baseline flags so we don't drift
/// across call sites. Always seeds: -hide_banner -nostats -loglevel warning -y.
/// </summary>
public static class FfmpegArgs
{
    public static IEnumerable<string> Baseline =>
        ["-hide_banner", "-nostats", "-loglevel", "warning", "-y", "-vn"];

    /// <summary>Codec + format-specific flags for the chosen output format.</summary>
    public static IEnumerable<string> Codec(AudioFormat format) =>
        format switch
        {
            AudioFormat.Flac =>
            [
                "-codec:a",
                "flac",
                "-compression_level",
                "8",
                "-bits_per_raw_sample",
                "24",
            ],
            AudioFormat.Wav => ["-codec:a", "pcm_s24le"],
            AudioFormat.Mp3 => ["-codec:a", "libmp3lame", "-b:a", "320k"],
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
}

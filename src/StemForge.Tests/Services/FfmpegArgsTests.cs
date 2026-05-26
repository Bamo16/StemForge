using StemForge.Helpers;
using StemForge.Models;

namespace StemForge.Tests.Services;

public sealed class FfmpegArgsTests
{
    // ── Baseline ─────────────────────────────────────────────────────────────

    [Fact]
    public void Baseline_ContainsRequiredFlags()
    {
        var args = FfmpegArgs.Baseline.ToList();
        Assert.Contains("-y", args);
        Assert.Contains("-hide_banner", args);
        Assert.Contains("-vn", args);
    }

    // ── Codec ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Codec_Flac_ContainsCorrectFlags()
    {
        var args = FfmpegArgs.Codec(AudioFormat.Flac).ToList();
        Assert.Contains("-codec:a", args);
        Assert.Contains("flac", args);
    }

    [Fact]
    public void Codec_Wav_ContainsPcmS24le()
    {
        var args = FfmpegArgs.Codec(AudioFormat.Wav).ToList();
        Assert.Contains("pcm_s24le", args);
    }

    [Fact]
    public void Codec_Mp3_ContainsLibmp3lameAnd320k()
    {
        var args = FfmpegArgs.Codec(AudioFormat.Mp3).ToList();
        Assert.Contains("libmp3lame", args);
        Assert.Contains("320k", args);
    }

    // ── Extension ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AudioFormat.Flac, "flac")]
    [InlineData(AudioFormat.Wav, "wav")]
    [InlineData(AudioFormat.Mp3, "mp3")]
    public void Extension_ReturnsCorrectString(AudioFormat format, string expected)
    {
        Assert.Equal(expected, FfmpegArgs.Extension(format));
    }
}

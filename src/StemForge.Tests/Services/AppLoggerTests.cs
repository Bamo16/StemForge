using StemForge.Core.Services;

namespace StemForge.Tests.Services;

public sealed class AppLoggerTests
{
    [Fact]
    public void Redact_MasksIpQueryParamValue_PreservingTheRest()
    {
        const string input =
            "ffmpeg -i https://rr4.googlevideo.com/videoplayback?expire=123&ip=2606%3Aa300%3A900f&id=abc -ar 44100 out.flac";

        var result = AppLogger.Redact(input);

        Assert.Contains("ip=<redacted>", result);
        Assert.DoesNotContain("2606", result); // the IP value is gone
        Assert.Contains("expire=123", result); // a param before ip= is kept
        Assert.Contains("id=abc", result); // a param after ip= is kept
        Assert.Contains("-ar 44100 out.flac", result); // trailing args are kept
    }

    [Fact]
    public void Redact_HandlesIpAsFirstQueryParam()
    {
        var result = AppLogger.Redact("https://host/path?ip=1.2.3.4&x=y");

        Assert.Contains("ip=<redacted>", result);
        Assert.DoesNotContain("1.2.3.4", result);
        Assert.Contains("x=y", result);
    }

    [Fact]
    public void Redact_LeavesMessageWithoutIpParamUnchanged()
    {
        const string input = "Loading model bs_roformer_vocals_revive_v3e_unwa.ckpt";
        Assert.Equal(input, AppLogger.Redact(input));
    }

    [Fact]
    public void Redact_DoesNotMatchIpSubstringInProse()
    {
        // "zip=" / "skip=" contain "ip=" but are not query parameters (no ? or & before).
        const string input = "set zip=archive.zip and skip=true";
        Assert.Equal(input, AppLogger.Redact(input));
    }
}

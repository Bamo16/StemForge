using System.Runtime.InteropServices;

namespace StemForge.Tests.Downloading;

public sealed class CookieSourceTests
{
    private static readonly PlatformInfo Windows = new(OSKind.Windows, Architecture.X64);
    private static readonly PlatformInfo Linux = new(OSKind.Linux, Architecture.X64);

    [Theory]
    [InlineData("chrome")]
    [InlineData("Edge")]
    [InlineData("chrome:Profile 1")]
    [InlineData("brave+basictext:Default")]
    public void IsLikelyUnreadable_ChromiumBrowserOnWindows_IsTrue(string cookies) =>
        Assert.True(CookieSource.IsLikelyUnreadable(cookies, Windows));

    [Theory]
    [InlineData("firefox")]
    [InlineData("firefox:0gx213hv.default-release")]
    [InlineData(@"C:\cookies\chrome.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void IsLikelyUnreadable_FirefoxFileOrNothing_IsFalse(string? cookies) =>
        Assert.False(CookieSource.IsLikelyUnreadable(cookies, Windows));

    [Fact]
    public void IsLikelyUnreadable_ChromeOffWindows_IsFalse() =>
        Assert.False(CookieSource.IsLikelyUnreadable("chrome", Linux));
}

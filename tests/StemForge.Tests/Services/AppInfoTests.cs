namespace StemForge.Tests.Services;

public sealed class AppInfoTests
{
    [Fact]
    public void ProductName_IsPassedThrough()
    {
        var info = new AppInfo("StemForge", new Version(0, 1, 1, 0));

        Assert.Equal("StemForge", info.ProductName);
    }

    [Fact]
    public void ShortVersion_IsThreePart()
    {
        var info = new AppInfo("StemForge", new Version(1, 2, 3, 4));

        Assert.Equal("1.2.3", info.ShortVersion);
    }

    [Fact]
    public void FullVersion_IncludesAllParts()
    {
        var info = new AppInfo("StemForge", new Version(1, 2, 3, 4));

        Assert.Equal("1.2.3.4", info.FullVersion);
    }

    [Fact]
    public void NullVersion_FallsBackToDev()
    {
        var info = new AppInfo("StemForge", version: null);

        Assert.Equal("dev", info.ShortVersion);
        Assert.Equal("dev", info.FullVersion);
    }

    [Fact]
    public void Current_ResolvesProductAndVersionFromAssembly()
    {
        IAppInfo info = AppInfo.Current;

        Assert.Equal("StemForge", info.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(info.ShortVersion));
    }
}

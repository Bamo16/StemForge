using Microsoft.Extensions.Time.Testing;
using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Tooling;

public sealed class YtdlpUpdaterTests : IDisposable
{
    private readonly string _bin = Path.Combine(
        Path.GetTempPath(),
        $"stemforge-bin-{Guid.NewGuid():N}"
    );
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)
    );
    private readonly FakeProcessRunner _runner = new();

    public void Dispose()
    {
        if (Directory.Exists(_bin))
            Directory.Delete(_bin, recursive: true);
    }

    [Fact]
    public async Task EnsureCurrent_NeverChecked_RunsUpdateOnTheBundledCopy()
    {
        var (updater, paths) = Build();

        Assert.True(await updater.EnsureCurrentAsync(Ct));

        var (exe, args) = Assert.Single(_runner.Calls);
        Assert.Equal(paths.BundledYtdlp, exe);
        Assert.Equal(["--update"], args);
    }

    [Fact]
    public async Task EnsureCurrent_CheckedWithinADay_DoesNothing()
    {
        var (updater, _) = Build();
        await updater.EnsureCurrentAsync(Ct);

        _time.Advance(TimeSpan.FromHours(23));

        Assert.False(await updater.EnsureCurrentAsync(Ct));
        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task EnsureCurrent_ADayLater_ChecksAgainEvenInANewSession()
    {
        var (first, _) = Build();
        await first.EnsureCurrentAsync(Ct);

        _time.Advance(TimeSpan.FromDays(1));
        var (second, _) = Build();

        Assert.True(await second.EnsureCurrentAsync(Ct));
        Assert.Equal(2, _runner.Calls.Count);
    }

    [Fact]
    public async Task EnsureCurrent_ReinstalledSinceTheLastCheck_ChecksAgain()
    {
        var (updater, paths) = Build();
        await updater.EnsureCurrentAsync(Ct);

        File.SetLastWriteTimeUtc(paths.BundledYtdlp, DateTime.UtcNow.AddHours(1));

        Assert.True(await updater.EnsureCurrentAsync(Ct));
    }

    [Fact]
    public async Task EnsureCurrent_OverriddenYtdlp_IsLeftAlone()
    {
        var settings = new AppSettings();
        settings.SetToolPathOverride(ToolKind.Ytdlp, @"C:\Tools\yt-dlp.exe");
        var (updater, _) = Build(settings);

        Assert.False(await updater.EnsureCurrentAsync(Ct));
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task EnsureCurrent_BundledCopyMissing_DoesNothingUntilItIsInstalled()
    {
        var (updater, paths) = Build(install: false);

        Assert.False(await updater.EnsureCurrentAsync(Ct));

        File.WriteAllText(paths.BundledYtdlp, string.Empty);
        Assert.True(await updater.EnsureCurrentAsync(Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (YtdlpUpdater Updater, AppPaths Paths) Build(
        AppSettings? settings = null,
        bool install = true
    )
    {
        var paths = new AppPaths(settings ?? new AppSettings(), bundledBinDir: _bin);
        Directory.CreateDirectory(_bin);
        if (install)
            File.WriteAllText(paths.BundledYtdlp, string.Empty);
        _runner.Setup(paths.BundledYtdlp, "yt-dlp is up to date");

        return (new YtdlpUpdater(_runner, paths, _time), paths);
    }
}

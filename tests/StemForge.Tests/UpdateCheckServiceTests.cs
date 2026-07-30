namespace StemForge.Tests;

public sealed class UpdateCheckServiceTests
{
    // ── IReleaseFetcher stubs ────────────────────────────────────────────────

    private static UpdateCheckService Build(string? tagToReturn, string runningVersion = "0.2.0")
    {
        var appInfo = new AppInfo("StemForge", Version.Parse(runningVersion + ".0"));
        var fetcher = new StubReleaseFetcher(tagToReturn);
        return new UpdateCheckService(appInfo, fetcher);
    }

    private sealed class StubReleaseFetcher(string? tag) : IReleaseFetcher
    {
        public Task<string?> FetchLatestTagAsync(CancellationToken ct = default) =>
            Task.FromResult(tag);
    }

    private sealed class ThrowingFetcher : IReleaseFetcher
    {
        public Task<string?> FetchLatestTagAsync(CancellationToken ct = default) =>
            throw new HttpRequestException("simulated network failure");
    }

    // ── Version comparison ───────────────────────────────────────────────────

    [Fact]
    public async Task NewerRelease_ReturnsUpdateAvailable()
    {
        var svc = Build(tagToReturn: "v0.3.0");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("0.3.0", result.LatestVersion);
    }

    [Fact]
    public async Task SameRelease_ReturnsNoUpdate()
    {
        var svc = Build(tagToReturn: "v0.2.0");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task OlderRelease_ReturnsNoUpdate()
    {
        var svc = Build(tagToReturn: "v0.1.0");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
    }

    // ── v-prefix handling ────────────────────────────────────────────────────

    [Fact]
    public async Task TagWithLowercaseVPrefix_ParsedCorrectly()
    {
        var svc = Build(tagToReturn: "v0.3.0");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("0.3.0", result.LatestVersion);
    }

    [Fact]
    public async Task TagWithUppercaseVPrefix_ParsedCorrectly()
    {
        var svc = Build(tagToReturn: "V0.3.0");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("0.3.0", result.LatestVersion);
    }

    [Fact]
    public async Task TagWithoutVPrefix_ParsedCorrectly()
    {
        var svc = Build(tagToReturn: "0.3.0");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.UpdateAvailable);
    }

    // ── Malformed / missing tag ──────────────────────────────────────────────

    [Fact]
    public async Task MalformedTag_ReturnsNoUpdate()
    {
        var svc = Build(tagToReturn: "not-a-version");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public async Task NullTag_ReturnsNoUpdate()
    {
        var svc = Build(tagToReturn: null);

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public async Task EmptyTag_ReturnsNoUpdate()
    {
        var svc = Build(tagToReturn: "");

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
    }

    // ── Silent failure on network error ─────────────────────────────────────

    [Fact]
    public async Task NetworkFailure_ReturnsNoUpdateWithoutThrowing()
    {
        var appInfo = new AppInfo("StemForge", new Version(0, 2, 0, 0));
        var svc = new UpdateCheckService(appInfo, new ThrowingFetcher());

        // Must not throw; must report no update available.
        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    // ── Four-component (hotfix) versions ─────────────────────────────────────

    [Fact]
    public async Task RunningVersionMatchesFourPartReleaseTag_ReturnsNoUpdate()
    {
        // The running build reports a 4-component version (a 0.2.1.1 hotfix) and the latest release
        // tag is that same version. It must not report an update against itself. Regression for
        // comparing the full version rather than the 3-component ShortVersion, which drops the 4th
        // part and made the app perpetually offer the version it was already running.
        var appInfo = new AppInfo("StemForge", new Version(0, 2, 1, 1));
        var svc = new UpdateCheckService(appInfo, new StubReleaseFetcher("v0.2.1.1"));

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task NewerFourPartReleaseTag_ReturnsUpdateAvailable()
    {
        var appInfo = new AppInfo("StemForge", new Version(0, 2, 1, 1));
        var svc = new UpdateCheckService(appInfo, new StubReleaseFetcher("v0.2.1.2"));

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.UpdateAvailable);
    }

    // ── Running version edge cases ───────────────────────────────────────────

    [Fact]
    public async Task RunningVersionIsDev_ReturnsNoUpdate()
    {
        // AppInfo with null version falls back to "dev", which does not parse as a Version.
        var appInfo = new AppInfo("StemForge", version: null);
        var svc = new UpdateCheckService(appInfo, new StubReleaseFetcher("v1.0.0"));

        var result = await svc.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(result.UpdateAvailable);
    }

    // ── Repo constants sanity ────────────────────────────────────────────────

    [Fact]
    public void GitHubReleaseFetcher_RepoConstants_MatchExpectedValues()
    {
        Assert.Equal("Bamo16", GitHubReleaseFetcher.RepoOwner);
        Assert.Equal("StemForge", GitHubReleaseFetcher.RepoName);
    }
}

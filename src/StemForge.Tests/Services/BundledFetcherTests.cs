using System.Net;
using System.Runtime.InteropServices;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.Tests.Services;

public sealed class BundledFetcherTests
{
    // sample-bin.tar.xz contains: pkg/bin/dummy, pkg/bin/extra.so, pkg/doc/README.
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "sample-bin.tar.xz"
    );

    [Fact]
    public void ExtractToDirectory_TarXz_FlattenFromBin_LandsTargetInBundleDir()
    {
        var targetDir = CreateTempDir();
        try
        {
            var asset = new BundledAsset(
                Url: "unused",
                Sha256: "unused",
                Format: ArchiveFormat.TarXz,
                Layout: BundledLayout.FlattenFromBinSubdir
            );

            BundledFetcher.ExtractToDirectory(asset, FixturePath, "dummy", targetDir);

            var landed = Path.Combine(targetDir, "dummy");
            Assert.True(
                File.Exists(landed),
                "target binary should land flattened in the bundle dir"
            );
            Assert.Equal("dummy-binary-contents\n", File.ReadAllText(landed).Replace("\r\n", "\n"));

            // Siblings under bin/ come along; files outside bin/ do not.
            Assert.True(File.Exists(Path.Combine(targetDir, "extra.so")));
            Assert.False(File.Exists(Path.Combine(targetDir, "README")));
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_TarXz_FixtureExists()
    {
        Assert.True(File.Exists(FixturePath), $"missing test fixture: {FixturePath}");
    }

    [Fact]
    public async Task DownloadAsync_SendsAppInfoUserAgent_AndReportsProgress()
    {
        var payload = new byte[2_500_000]; // > 1 MiB so progress throttling fires at least once.
        Random.Shared.NextBytes(payload);

        var fetcher = NewFetcher(new AppInfo("StemForgeTest", new Version(9, 8, 7)));
        var seenUserAgents = new List<string?>();

        using var server = new LoopbackServer(payload, seenUserAgents);
        var dest1 = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
        var dest2 = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
        var reports = new List<InstallProgress>();
        var progress = new SynchronousProgress(reports.Add);

        var ct = TestContext.Current.CancellationToken;
        try
        {
            await fetcher.DownloadAsync(server.Url, dest1, progress, ct);
            // A second download must succeed on the same shared client (no socket churn / disposal).
            await fetcher.DownloadAsync(server.Url, dest2, progress, ct);

            Assert.Equal(payload, await File.ReadAllBytesAsync(dest1, ct));
            Assert.Equal(payload, await File.ReadAllBytesAsync(dest2, ct));

            // User-agent sourced from IAppInfo (ProductName/ShortVersion), on every request.
            Assert.Equal(2, seenUserAgents.Count);
            Assert.All(seenUserAgents, ua => Assert.Equal("StemForgeTest/9.8.7", ua));

            // Progress was reported, ending at a full read with the known total.
            Assert.NotEmpty(reports);
            Assert.Contains(reports, r => r.BytesDownloaded == payload.Length);
        }
        finally
        {
            File.Delete(dest1);
            File.Delete(dest2);
        }
    }

    private static BundledFetcher NewFetcher(IAppInfo appInfo)
    {
        var paths = new AppPaths(new AppSettings());
        var platform = new PlatformInfo(OSKind.Windows, Architecture.X64);
        var factory = new BundledHttpClientFactory(appInfo);
        return new BundledFetcher(paths, platform, factory);
    }

    /// <summary>
    /// Minimal <see cref="IHttpClientFactory"/> that returns a single shared
    /// <see cref="HttpClient"/> configured identically to the "bundled" named client
    /// registered in production, so tests exercise the same User-Agent and timeout.
    /// </summary>
    private sealed class BundledHttpClientFactory(IAppInfo appInfo) : IHttpClientFactory
    {
        private readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestHeaders =
            {
                UserAgent = { new(appInfo.ProductName, appInfo.ShortVersion) },
            },
        };

        public HttpClient CreateClient(string name) => _client;
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemforge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Reports each <see cref="InstallProgress"/> synchronously on the calling thread.</summary>
    private sealed class SynchronousProgress(Action<InstallProgress> onReport)
        : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => onReport(value);
    }

    /// <summary>
    /// Minimal loopback HTTP server that serves a fixed payload and records the User-Agent header of
    /// every request it answers. Used to exercise the shared-client download path end to end.
    /// </summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serve;

        public LoopbackServer(byte[] payload, List<string?> seenUserAgents)
        {
            var port = GetFreePort();
            Url = $"http://127.0.0.1:{port}/asset";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _serve = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                    }
                    catch (HttpListenerException)
                    {
                        return; // listener stopped
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    lock (seenUserAgents)
                        seenUserAgents.Add(ctx.Request.UserAgent);

                    ctx.Response.ContentLength64 = payload.Length;
                    await ctx.Response.OutputStream.WriteAsync(payload);
                    ctx.Response.Close();
                }
            });
        }

        public string Url { get; }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try
            {
                _serve.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // best-effort shutdown
            }
        }

        private static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}

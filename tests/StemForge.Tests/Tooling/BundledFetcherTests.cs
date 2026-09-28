using System.IO.Compression;
using System.Net;

namespace StemForge.Tests.Tooling;

public sealed class BundledFetcherTests
{
    // sample-root.tar.gz contains: ./dummy, ./dummy-probe, ./dummy-play, ./nested/dummy.
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "sample-root.tar.gz"
    );

    private static readonly BundledAsset TarGzAtRoot = new(
        Url: "unused",
        Sha256: "unused",
        Format: ArchiveFormat.TarGz,
        Layout: BundledLayout.FilesAtRoot
    );

    [Fact]
    public void ExtractToDirectory_TarGz_TakesOnlyTheNamedRootFiles()
    {
        var targetDir = CreateTempDir();
        try
        {
            BundledFetcher.ExtractToDirectory(
                TarGzAtRoot,
                FixturePath,
                ["dummy", "dummy-probe"],
                targetDir
            );

            Assert.Equal(
                "dummy-binary-contents\n",
                File.ReadAllText(Path.Combine(targetDir, "dummy")).Replace("\r\n", "\n")
            );
            Assert.True(File.Exists(Path.Combine(targetDir, "dummy-probe")));
            Assert.False(File.Exists(Path.Combine(targetDir, "dummy-play")));
            Assert.Equal(["dummy", "dummy-probe"], FileNamesIn(targetDir));
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_MissingCompanion_Throws()
    {
        var targetDir = CreateTempDir();
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() =>
                BundledFetcher.ExtractToDirectory(
                    TarGzAtRoot,
                    FixturePath,
                    ["dummy", "dummy-missing"],
                    targetDir
                )
            );
            Assert.Contains("dummy-missing", ex.Message);
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractToDirectory_Zip_MatchesRootEntriesOnly()
    {
        var targetDir = CreateTempDir();
        var zipPath = Path.Combine(targetDir, "sample.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "nested/tool.exe", "nested");
            WriteEntry(zip, "tool.exe", "root");
        }

        var outDir = Path.Combine(targetDir, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            BundledFetcher.ExtractToDirectory(
                TarGzAtRoot with
                {
                    Format = ArchiveFormat.Zip,
                },
                zipPath,
                ["tool.exe"],
                outDir
            );

            Assert.Equal("root", File.ReadAllText(Path.Combine(outDir, "tool.exe")));
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void MarkExecutable_OnUnix_SetsExecuteBits()
    {
        // The return is for the platform analyzer, which does not know Skip throws.
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Execute bits exist only on Linux and macOS.");
            return;
        }

        var targetDir = CreateTempDir();
        var file = Path.Combine(targetDir, "tool");
        File.WriteAllText(file, "#!/bin/sh\n");
        try
        {
            BundledFetcher.MarkExecutable([file]);

            Assert.True(File.GetUnixFileMode(file).HasFlag(UnixFileMode.UserExecute));
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string contents)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(contents);
    }

    private static List<string> FileNamesIn(string dir) =>
        [.. Directory.GetFiles(dir).Select(Path.GetFileName).OfType<string>().Order()];

    [Fact]
    public async Task FileDownloader_DownloadAsync_SendsUserAgent_AndReportsProgress()
    {
        var payload = new byte[2_500_000]; // > 1 MiB so progress throttling fires at least once.
        Random.Shared.NextBytes(payload);

        var appInfo = new AppInfo("StemForgeTest", new Version(9, 8, 7));
        var downloader = NewDownloader(appInfo);
        var seenUserAgents = new List<string?>();

        using var server = new LoopbackServer(payload, seenUserAgents);
        var dest1 = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
        var dest2 = Path.Combine(Path.GetTempPath(), $"stemforge-dl-{Guid.NewGuid():N}");
        var reports = new List<InstallProgress>();
        var progress = new SynchronousProgress(reports.Add);

        var ct = TestContext.Current.CancellationToken;
        try
        {
            await downloader.DownloadAsync(server.Url, dest1, progress, ct);
            // A second download must succeed on the same shared client (no socket churn / disposal).
            await downloader.DownloadAsync(server.Url, dest2, progress, ct);

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

    private static FileDownloader NewDownloader(IAppInfo appInfo) =>
        new(
            new TestHttpClientFactory(
                new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(15),
                    DefaultRequestHeaders =
                    {
                        UserAgent = { new(appInfo.ProductName, appInfo.ShortVersion) },
                    },
                }
            )
        );

    /// <summary>Returns the same <see cref="HttpClient"/> on every <see cref="CreateClient"/> call.</summary>
    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
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

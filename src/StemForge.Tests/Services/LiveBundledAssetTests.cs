using System.Runtime.InteropServices;
using System.Security.Cryptography;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Network-dependent tests that actually download the linux-x64 bundled binaries (yt-dlp, ffmpeg,
/// deno) and verify their pinned SHA-256, plus exercise the tar.xz extraction path for ffmpeg.
/// These are SKIPPED by default so the normal <c>dotnet test</c> suite stays offline and fast;
/// they run only when <c>STEMFORGE_LIVE_ASSETS=1</c> is set (the dedicated CI step does so). This
/// is what proves the pinned URLs and hashes are still live and correct.
/// </summary>
public sealed class LiveBundledAssetTests
{
    private const string EnvGate = "STEMFORGE_LIVE_ASSETS";

    private static readonly PlatformInfo LinuxX64 = new(OSKind.Linux, Architecture.X64);

    /// <summary>Referenced by <c>SkipUnless</c>: true only when the live-assets env gate is set.</summary>
    public static bool LiveAssetsEnabled =>
        Environment.GetEnvironmentVariable(EnvGate) is "1" or "true" or "TRUE";

    private static HttpClient NewHttpClient() => new() { Timeout = TimeSpan.FromMinutes(15) };

    public static IEnumerable<object[]> LinuxBundledTools =>
        ToolCatalog
            .All.Where(t => t.InstallStrategy is BundledFetch)
            .Select(t => new object[] { t.Kind });

    [Theory(SkipUnless = nameof(LiveAssetsEnabled), Skip = "Set STEMFORGE_LIVE_ASSETS=1 to run.")]
    [MemberData(nameof(LinuxBundledTools))]
    public async Task LinuxAsset_DownloadsAndMatchesPinnedSha256(ToolKind kind)
    {
        var tool = ToolCatalog.Get(kind);
        var asset = Assert.IsType<BundledFetch>(tool.InstallStrategy).AssetFor(LinuxX64);
        Assert.NotNull(asset);

        var ct = TestContext.Current.CancellationToken;
        var temp = await DownloadToTempAsync(asset.Url, ct);
        try
        {
            var actual = await ComputeSha256Async(temp, ct);
            Assert.Equal(asset.Sha256.ToLowerInvariant(), actual);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact(SkipUnless = nameof(LiveAssetsEnabled), Skip = "Set STEMFORGE_LIVE_ASSETS=1 to run.")]
    public async Task LinuxFfmpeg_TarXz_ExtractsBinaryViaTarXzPath()
    {
        var tool = ToolCatalog.Get(ToolKind.Ffmpeg);
        var asset = Assert.IsType<BundledFetch>(tool.InstallStrategy).AssetFor(LinuxX64);
        Assert.NotNull(asset);
        Assert.Equal(ArchiveFormat.TarXz, asset.Format);

        var ct = TestContext.Current.CancellationToken;
        var archive = await DownloadToTempAsync(asset.Url, ct);
        var targetDir = Path.Combine(Path.GetTempPath(), $"stemforge-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        try
        {
            var actual = await ComputeSha256Async(archive, ct);
            Assert.Equal(asset.Sha256.ToLowerInvariant(), actual);

            // Linux ffmpeg binary has no extension; exercise the real tar.xz extraction path.
            var binaryName = tool.BundledBinaryFileName(LinuxX64);
            Assert.Equal("ffmpeg", binaryName);

            BundledFetcher.ExtractToDirectory(asset, archive, binaryName, targetDir);

            var landed = Path.Combine(targetDir, binaryName);
            Assert.True(
                File.Exists(landed),
                $"{binaryName} should be flattened into the bundle dir"
            );
            Assert.True(new FileInfo(landed).Length > 0, "extracted ffmpeg should be non-empty");
        }
        finally
        {
            TryDelete(archive);
            try
            {
                Directory.Delete(targetDir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private static async Task<string> DownloadToTempAsync(string url, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"stemforge-live-{Guid.NewGuid():N}");
        using var http = NewHttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = File.Create(temp);
        await src.CopyToAsync(dest, ct);
        return temp;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

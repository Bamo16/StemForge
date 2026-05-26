using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace StemForge.Services;

/// <summary>
/// Reports progress while downloading the bundled ffmpeg archive.
/// </summary>
public sealed record FfmpegFetchProgress(long BytesDownloaded, long? TotalBytes, string Phase);

/// <summary>
/// Downloads and extracts the bundled ffmpeg binary from the pinned yt-dlp/FFmpeg-Builds
/// release. The whole point of doing this ourselves is sidestepping the "winget added it to
/// PATH but the running app can't see it until restart" problem — we drop ffmpeg.exe (and
/// its shared DLLs) into <see cref="AppPaths.BundledBinDir"/>, and <see cref="ProcessRunner"/>
/// prepends that directory to every child process's PATH.
/// </summary>
public sealed class FfmpegFetcher(AppPaths paths)
{
    // Pinned to a specific dated autobuild from
    // https://github.com/yt-dlp/FFmpeg-Builds/releases (same project that maintains yt-dlp,
    // so trust chain stays narrow). Dated-tag assets carry the actual build commit hash in
    // the filename — unlike the rolling 'latest' tag which uses "master-latest". To bump,
    // pick a newer autobuild release, copy its win64-gpl-shared asset URL verbatim, and
    // update ExpectedSha256 to match.
    private const string DownloadUrl =
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/"
        + "autobuild-2026-05-26-17-26/"
        + "ffmpeg-N-124653-g0ac3b00a18-win64-gpl-shared.zip";

    // SHA256 of the pinned asset. Empty string disables verification (logged as a warning
    // by the install flow). Bump this in lockstep with DownloadUrl whenever the pinned
    // autobuild release moves — compute via `Get-FileHash -Algorithm SHA256` on Windows or
    // `sha256sum` on Linux/macOS against the actual downloaded zip.
    private const string ExpectedSha256 =
        "5ea46ea816a48f48e0d4c2ccf5997b4201bc8bed0be8ef05ccd169dc91d11dee";

    private readonly AppPaths _paths = paths;

    /// <summary>Returns true when the bundled ffmpeg binary is already present.</summary>
    public bool IsFfmpegBundled => File.Exists(_paths.BundledFfmpeg);

    /// <summary>
    /// Downloads, verifies and extracts the bundled ffmpeg archive. The archive contains
    /// ffmpeg.exe + the shared-build DLLs under a single top-level directory; we extract
    /// only files from any subpath that ends in <c>/bin/</c> and flatten them into
    /// <see cref="AppPaths.BundledBinDir"/>.
    /// </summary>
    public async Task FetchAsync(
        IProgress<FfmpegFetchProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        Directory.CreateDirectory(_paths.BundledBinDir);

        var tempZip = Path.Combine(Path.GetTempPath(), $"stemforge-ffmpeg-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(tempZip, progress, ct);
            VerifyChecksum(tempZip, progress);
            ExtractBinDir(tempZip, progress);
        }
        finally
        {
            try
            {
                File.Delete(tempZip);
            }
            catch
            {
                // best-effort cleanup; %TEMP% will eventually be reaped
            }
        }
    }

    private static async Task DownloadAsync(
        string destinationZip,
        IProgress<FfmpegFetchProgress>? progress,
        CancellationToken ct
    )
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestHeaders = { UserAgent = { new("StemForge", "0.1") } },
        };

        using var response = await http.GetAsync(
            DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(destinationZip);

        var buffer = new byte[81920];
        long totalRead = 0;
        long lastReported = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            // Throttle progress reports to once per ~1 MiB to keep the log readable.
            if (totalRead - lastReported >= 1_048_576 || totalRead == totalBytes)
            {
                progress?.Report(new FfmpegFetchProgress(totalRead, totalBytes, "Downloading"));
                lastReported = totalRead;
            }
        }
    }

    private static void VerifyChecksum(string zipPath, IProgress<FfmpegFetchProgress>? progress)
    {
        if (string.IsNullOrEmpty(ExpectedSha256))
        {
            progress?.Report(
                new FfmpegFetchProgress(0, null, "Skipping checksum (no pinned hash)")
            );
            return;
        }

        progress?.Report(new FfmpegFetchProgress(0, null, "Verifying checksum"));

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(zipPath);
        var hashBytes = sha.ComputeHash(stream);
        var actualHash = Convert.ToHexStringLower(hashBytes);

        if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"ffmpeg download checksum mismatch.\n  expected: {ExpectedSha256}\n  actual:   {actualHash}"
            );
        }
    }

    private void ExtractBinDir(string zipPath, IProgress<FfmpegFetchProgress>? progress)
    {
        progress?.Report(new FfmpegFetchProgress(0, null, "Extracting"));

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            // Match any entry under a 'bin/' subdirectory at any depth. Flatten into BundledBinDir.
            var idx = entry.FullName.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || entry.FullName.EndsWith('/'))
                continue;

            var relativeName = entry.FullName[(idx + "/bin/".Length)..];
            if (relativeName.Length == 0 || relativeName.Contains('/'))
                continue; // skip nested subdirs under bin/ (none expected in this archive)

            var destination = Path.Combine(_paths.BundledBinDir, relativeName);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }
}

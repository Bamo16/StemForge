using System.IO.Compression;
using System.Security.Cryptography;

namespace StemForge.Services;

/// <summary>
/// Reports progress while downloading the bundled deno archive.
/// </summary>
public sealed record DenoFetchProgress(long BytesDownloaded, long? TotalBytes, string Phase);

/// <summary>
/// Downloads and extracts the bundled deno binary from a pinned denoland/deno release into
/// <see cref="AppPaths.BundledBinDir"/>. Mirrors <see cref="FfmpegFetcher"/>: same prepend-to-
/// child-PATH mechanism means yt-dlp auto-discovers deno as its JS runtime for solving
/// YouTube's n-challenges, without the user ever needing to install deno themselves.
/// </summary>
public sealed class DenoFetcher(AppPaths paths)
{
    // Bump together with ExpectedSha256 when updating. denoland/deno publishes a
    // companion .sha256sum file alongside each release asset — copy the hex digest out of
    // that, lowercase, no other formatting.
    private const string DownloadUrl =
        "https://github.com/denoland/deno/releases/download/v2.8.0/deno-x86_64-pc-windows-msvc.zip";
    private const string ExpectedSha256 =
        "9b98d1f456878c8ac5caa55779a04f2f1f91f8e942d6ef3f887681698f634adf";

    private readonly AppPaths _paths = paths;

    /// <summary>Returns true when the bundled deno binary is already present.</summary>
    public bool IsDenoBundled => File.Exists(_paths.BundledDeno);

    /// <summary>
    /// Downloads, verifies and extracts the bundled deno archive. The release zip contains
    /// a single <c>deno.exe</c> at the root; we drop it directly into
    /// <see cref="AppPaths.BundledBinDir"/>.
    /// </summary>
    public async Task FetchAsync(
        IProgress<DenoFetchProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        Directory.CreateDirectory(_paths.BundledBinDir);

        var tempZip = Path.Combine(Path.GetTempPath(), $"stemforge-deno-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(tempZip, progress, ct);
            VerifyChecksum(tempZip, progress);
            ExtractDeno(tempZip, progress);
        }
        finally
        {
            try
            {
                File.Delete(tempZip);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private static async Task DownloadAsync(
        string destinationZip,
        IProgress<DenoFetchProgress>? progress,
        CancellationToken ct
    )
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
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

            if (totalRead - lastReported >= 1_048_576 || totalRead == totalBytes)
            {
                progress?.Report(new DenoFetchProgress(totalRead, totalBytes, "Downloading"));
                lastReported = totalRead;
            }
        }
    }

    private static void VerifyChecksum(string zipPath, IProgress<DenoFetchProgress>? progress)
    {
        progress?.Report(new DenoFetchProgress(0, null, "Verifying checksum"));

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(zipPath);
        var hashBytes = sha.ComputeHash(stream);
        var actualHash = Convert.ToHexStringLower(hashBytes);

        if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"deno download checksum mismatch.\n  expected: {ExpectedSha256}\n  actual:   {actualHash}"
            );
        }
    }

    private void ExtractDeno(string zipPath, IProgress<DenoFetchProgress>? progress)
    {
        progress?.Report(new DenoFetchProgress(0, null, "Extracting"));

        using var archive = ZipFile.OpenRead(zipPath);
        var entry =
            archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("deno.exe", StringComparison.OrdinalIgnoreCase)
            ) ?? throw new InvalidDataException("deno.exe not found in downloaded archive.");

        var destination = Path.Combine(_paths.BundledBinDir, "deno.exe");
        entry.ExtractToFile(destination, overwrite: true);
    }
}

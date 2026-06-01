using System.IO.Compression;
using System.Security.Cryptography;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Downloads a tool's <see cref="BundledFetch"/> asset, verifies its SHA-256, and installs the
/// binary into <see cref="AppPaths.BundledBinDir"/>. Consolidates the former per-tool
/// FfmpegFetcher/DenoFetcher: the per-tool differences (raw exe vs archive layout) are expressed
/// by <see cref="ExtractMode"/> in the catalog rather than separate classes.
/// </summary>
public sealed class BundledFetcher(AppPaths paths, PlatformInfo platform)
{
    private readonly AppPaths _paths = paths;
    private readonly PlatformInfo _platform = platform;

    /// <summary>True when the tool's bundled binary is already present.</summary>
    public bool IsBundled(Tool tool) =>
        File.Exists(Path.Combine(_paths.BundledBinDir, tool.BundledBinaryFileName(_platform)));

    public async Task FetchAsync(
        Tool tool,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        if (tool.InstallStrategy is not BundledFetch strategy)
            throw new InvalidOperationException($"{tool.CliName} is not a bundled-fetch tool.");

        var asset =
            strategy.AssetFor(_platform)
            ?? throw new PlatformNotSupportedException(
                $"No bundled {tool.CliName} asset for {_platform.Os}/{_platform.Arch}."
            );

        Directory.CreateDirectory(_paths.BundledBinDir);

        var suffix =
            asset.ExtractMode == ExtractMode.RawBinary ? _platform.ExecutableSuffix : ".zip";
        var temp = Path.Combine(
            Path.GetTempPath(),
            $"stemforge-{tool.CliName}-{Guid.NewGuid():N}{suffix}"
        );
        try
        {
            await DownloadAsync(asset.Url, temp, progress, ct);
            VerifyChecksum(temp, asset.Sha256, progress);
            Install(asset.ExtractMode, temp, tool, progress);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // best-effort cleanup; %TEMP% will eventually be reaped
            }
        }
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        IProgress<InstallProgress>? progress,
        CancellationToken ct
    )
    {
        var appVersion =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "dev";
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestHeaders = { UserAgent = { new("StemForge", appVersion) } },
        };

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(destination);

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
                progress?.Report(new InstallProgress("Downloading", totalRead, totalBytes));
                lastReported = totalRead;
            }
        }
    }

    private static void VerifyChecksum(
        string path,
        string expectedSha256,
        IProgress<InstallProgress>? progress
    )
    {
        if (string.IsNullOrEmpty(expectedSha256))
        {
            progress?.Report(new InstallProgress("Skipping checksum (no pinned hash)"));
            return;
        }

        progress?.Report(new InstallProgress("Verifying checksum"));

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(sha.ComputeHash(stream));

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"download checksum mismatch.\n  expected: {expectedSha256}\n  actual:   {actual}"
            );
    }

    private void Install(
        ExtractMode mode,
        string downloaded,
        Tool tool,
        IProgress<InstallProgress>? progress
    )
    {
        progress?.Report(
            new InstallProgress(mode == ExtractMode.RawBinary ? "Installing" : "Extracting")
        );

        var binaryName = tool.BundledBinaryFileName(_platform);
        switch (mode)
        {
            case ExtractMode.RawBinary:
                File.Copy(
                    downloaded,
                    Path.Combine(_paths.BundledBinDir, binaryName),
                    overwrite: true
                );
                break;
            case ExtractMode.SingleFileAtRoot:
                ExtractSingleFile(downloaded, binaryName);
                break;
            case ExtractMode.FlattenFromBinSubdir:
                ExtractBinDir(downloaded);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private void ExtractSingleFile(string zipPath, string binaryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry =
            archive.Entries.FirstOrDefault(e =>
                e.Name.Equals(binaryName, StringComparison.OrdinalIgnoreCase)
            ) ?? throw new InvalidDataException($"{binaryName} not found in downloaded archive.");

        entry.ExtractToFile(Path.Combine(_paths.BundledBinDir, binaryName), overwrite: true);
    }

    private void ExtractBinDir(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            // Match any entry under a 'bin/' subdirectory at any depth. Flatten into BundledBinDir.
            var idx = entry.FullName.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || entry.FullName.EndsWith('/'))
                continue;

            var relativeName = entry.FullName[(idx + "/bin/".Length)..];
            if (relativeName.Length == 0 || relativeName.Contains('/'))
                continue; // skip nested subdirs under bin/ (none expected in these archives)

            entry.ExtractToFile(Path.Combine(_paths.BundledBinDir, relativeName), overwrite: true);
        }
    }
}

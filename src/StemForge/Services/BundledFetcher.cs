using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Compressors.Xz;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Downloads a tool's <see cref="BundledFetch"/> asset, verifies its SHA-256, and installs the
/// binary into <see cref="AppPaths.BundledBinDir"/>. Consolidates the former per-tool
/// FfmpegFetcher/DenoFetcher: the per-tool differences are expressed as two orthogonal axes on
/// the catalog's <see cref="BundledAsset"/> (<see cref="ArchiveFormat"/> and
/// <see cref="BundledLayout"/>) rather than separate classes.
/// </summary>
public sealed class BundledFetcher(AppPaths paths, PlatformInfo platform, IAppInfo appInfo)
{
    private readonly AppPaths _paths = paths;
    private readonly PlatformInfo _platform = platform;
    private readonly IAppInfo _appInfo = appInfo;

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

        var suffix = asset.Format switch
        {
            ArchiveFormat.RawBinary => _platform.ExecutableSuffix,
            ArchiveFormat.Zip => ".zip",
            ArchiveFormat.TarXz => ".tar.xz",
            _ => throw new ArgumentOutOfRangeException(nameof(asset)),
        };
        var temp = Path.Combine(
            Path.GetTempPath(),
            $"stemforge-{tool.CliName}-{Guid.NewGuid():N}{suffix}"
        );
        try
        {
            await DownloadAsync(asset.Url, temp, _appInfo, progress, ct);
            // SHA-256 is verified on the downloaded bytes before any extraction touches disk.
            VerifyChecksum(temp, asset.Sha256, progress);
            Install(asset, temp, tool, progress);
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
        IAppInfo appInfo,
        IProgress<InstallProgress>? progress,
        CancellationToken ct
    )
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestHeaders =
            {
                UserAgent = { new(appInfo.ProductName, appInfo.ShortVersion) },
            },
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
        BundledAsset asset,
        string downloaded,
        Tool tool,
        IProgress<InstallProgress>? progress
    )
    {
        var isRaw = asset.Format == ArchiveFormat.RawBinary;
        progress?.Report(new InstallProgress(isRaw ? "Installing" : "Extracting"));

        var binaryName = tool.BundledBinaryFileName(_platform);
        switch (asset.Layout)
        {
            case BundledLayout.DownloadIsBinary:
                File.Copy(
                    downloaded,
                    Path.Combine(_paths.BundledBinDir, binaryName),
                    overwrite: true
                );
                break;
            case BundledLayout.SingleFileAtRoot:
                ExtractToDirectory(asset, downloaded, binaryName, _paths.BundledBinDir);
                break;
            case BundledLayout.FlattenFromBinSubdir:
                ExtractToDirectory(asset, downloaded, binaryName, _paths.BundledBinDir);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(asset), asset.Layout, null);
        }
    }

    /// <summary>
    /// Extracts the target binary (and, for <see cref="BundledLayout.FlattenFromBinSubdir"/>, its
    /// sibling runtime files) from a zip or tar.xz archive into <paramref name="targetDir"/>.
    /// Exposed internally so the extraction logic is testable against a temp directory without
    /// hitting the real <see cref="AppPaths.BundledBinDir"/>.
    /// </summary>
    internal static void ExtractToDirectory(
        BundledAsset asset,
        string archivePath,
        string binaryName,
        string targetDir
    )
    {
        switch (asset.Layout)
        {
            case BundledLayout.SingleFileAtRoot:
                ExtractSingleFile(asset.Format, archivePath, binaryName, targetDir);
                break;
            case BundledLayout.FlattenFromBinSubdir:
                ExtractBinDir(asset.Format, archivePath, targetDir);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(asset), asset.Layout, null);
        }
    }

    private static void ExtractSingleFile(
        ArchiveFormat format,
        string archivePath,
        string binaryName,
        string targetDir
    )
    {
        var found = false;
        ForEachEntry(
            format,
            archivePath,
            (name, copyTo) =>
            {
                var leaf = name[(name.LastIndexOf('/') + 1)..];
                if (!found && leaf.Equals(binaryName, StringComparison.OrdinalIgnoreCase))
                {
                    using var dest = File.Create(Path.Combine(targetDir, binaryName));
                    copyTo(dest);
                    found = true;
                }
            }
        );

        if (!found)
            throw new InvalidDataException($"{binaryName} not found in downloaded archive.");
    }

    private static void ExtractBinDir(ArchiveFormat format, string archivePath, string targetDir)
    {
        ForEachEntry(
            format,
            archivePath,
            (name, copyTo) =>
            {
                // Match any entry under a 'bin/' subpath at any depth; flatten into targetDir.
                var idx = name.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return;

                var relativeName = name[(idx + "/bin/".Length)..];
                if (relativeName.Length == 0 || relativeName.Contains('/'))
                    return; // skip directory entries and nested subdirs under bin/ (none expected)

                using var dest = File.Create(Path.Combine(targetDir, relativeName));
                copyTo(dest);
            }
        );
    }

    /// <summary>
    /// Iterates the file entries of a zip or tar.xz archive, invoking <paramref name="onEntry"/>
    /// with the entry's full path (always forward-slash separated) and a callback that copies the
    /// entry's bytes into a destination stream. Directory entries are skipped.
    /// </summary>
    private static void ForEachEntry(
        ArchiveFormat format,
        string archivePath,
        Action<string, Action<Stream>> onEntry
    )
    {
        switch (format)
        {
            case ArchiveFormat.Zip:
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith('/'))
                            continue;
                        onEntry(
                            entry.FullName,
                            dest =>
                            {
                                using var src = entry.Open();
                                src.CopyTo(dest);
                            }
                        );
                    }
                }
                break;

            case ArchiveFormat.TarXz:
                // .NET 11 ships a tar reader (System.Formats.Tar) but no xz/LZMA decoder, so the
                // xz layer is peeled by SharpCompress and the resulting tar stream is read by the
                // BCL tar reader.
                using (var file = File.OpenRead(archivePath))
                using (var xz = new XZStream(file))
                using (var tar = new System.Formats.Tar.TarReader(xz))
                {
                    while (tar.GetNextEntry() is { } entry)
                    {
                        if (entry.EntryType is not System.Formats.Tar.TarEntryType.RegularFile)
                            continue;
                        onEntry(
                            entry.Name.Replace('\\', '/'),
                            dest => entry.DataStream?.CopyTo(dest)
                        );
                    }
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }
}

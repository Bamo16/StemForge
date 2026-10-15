using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace StemForge.Core.Tooling;

public interface IFileDownloader
{
    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destination"/>, reporting progress.
    /// </summary>
    Task DownloadAsync(
        string url,
        string destination,
        IProgress<InstallProgress>? progress,
        CancellationToken ct,
        string? toolName = null
    );
}

/// <summary>
/// Downloads a tool's <see cref="BundledFetch"/> asset, verifies its SHA-256, and installs the
/// binary into <see cref="AppPaths.BundledBinDir"/>. Consolidates the former per-tool
/// FfmpegFetcher/DenoFetcher: the per-tool differences are expressed as two orthogonal axes on
/// the catalog's <see cref="BundledAsset"/> (<see cref="ArchiveFormat"/> and
/// <see cref="BundledLayout"/>) rather than separate classes.
/// </summary>
public sealed class BundledFetcher(
    AppPaths paths,
    PlatformInfo platform,
    IFileDownloader fileDownloader
)
{
    private readonly AppPaths _paths = paths;
    private readonly PlatformInfo _platform = platform;
    private readonly IFileDownloader _fileDownloader = fileDownloader;

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
            ArchiveFormat.TarGz => ".tar.gz",
            _ => throw new UnreachableException(),
        };
        var temp = Path.Combine(
            Path.GetTempPath(),
            $"stemforge-{tool.CliName}-{Guid.NewGuid():N}{suffix}"
        );
        // Every log line is prefixed with the tool name so the wizard's cumulative multi-tool
        // log stays unambiguous about which tool a Downloading/Verifying/Extracting line belongs to.
        try
        {
            await _fileDownloader.DownloadAsync(asset.Url, temp, progress, ct, tool.CliName);
            // SHA-256 is verified on the downloaded bytes before any extraction touches disk.
            VerifyChecksum(temp, asset.Sha256, progress, tool.CliName);
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

    private static void VerifyChecksum(
        string path,
        string expectedSha256,
        IProgress<InstallProgress>? progress,
        string? toolName = null
    )
    {
        if (string.IsNullOrEmpty(expectedSha256))
        {
            progress?.Report(
                new InstallProgress(
                    InstallProgress.Prefix(toolName, "Skipping checksum (no pinned hash)")
                )
            );
            return;
        }

        progress?.Report(
            new InstallProgress(InstallProgress.Prefix(toolName, "Verifying checksum"))
        );

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
        progress?.Report(
            new InstallProgress(
                InstallProgress.Prefix(tool.CliName, isRaw ? "Installing" : "Extracting")
            )
        );

        var binaryName = tool.BundledBinaryFileName(_platform);
        IReadOnlyList<string> fileNames =
        [
            binaryName,
            .. (asset.Companions ?? []).Select(companion => companion + _platform.ExecutableSuffix),
        ];

        switch (asset.Layout)
        {
            case BundledLayout.DownloadIsBinary:
                File.Copy(
                    downloaded,
                    Path.Combine(_paths.BundledBinDir, binaryName),
                    overwrite: true
                );
                break;
            case BundledLayout.FilesAtRoot:
                ExtractToDirectory(asset, downloaded, fileNames, _paths.BundledBinDir);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(asset), asset.Layout, null);
        }

        MarkExecutable(fileNames.Select(name => Path.Combine(_paths.BundledBinDir, name)));
    }

    /// <summary>
    /// Extracts <paramref name="fileNames"/> from the root of a zip or tar.gz archive into
    /// <paramref name="targetDir"/>, failing if any is missing.
    /// </summary>
    internal static void ExtractToDirectory(
        BundledAsset asset,
        string archivePath,
        IReadOnlyList<string> fileNames,
        string targetDir
    )
    {
        if (asset.Layout is not BundledLayout.FilesAtRoot)
            throw new ArgumentOutOfRangeException(nameof(asset), asset.Layout, null);

        var missing = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        ForEachEntry(
            asset.Format,
            archivePath,
            (name, copyTo) =>
            {
                // Tar entries are spelled "./ffmpeg"; zip entries plain "ffmpeg".
                var relative = name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name;
                if (relative.Contains('/') || !missing.Remove(relative))
                    return;

                using var dest = File.Create(Path.Combine(targetDir, relative));
                copyTo(dest);
            }
        );

        if (missing.Count > 0)
            throw new InvalidDataException(
                $"{string.Join(", ", missing)} not found at the root of the downloaded archive."
            );
    }

    /// <summary>
    /// Sets the execute bits an extracted or downloaded file does not carry on Linux and macOS,
    /// where it would otherwise land as 0644 and fail to start.
    /// </summary>
    internal static void MarkExecutable(IEnumerable<string> paths)
    {
        if (OperatingSystem.IsWindows())
            return;

        foreach (var path in paths)
            File.SetUnixFileMode(
                path,
                File.GetUnixFileMode(path)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute
            );
    }

    /// <summary>
    /// Iterates the file entries of a zip or tar.gz archive, invoking <paramref name="onEntry"/>
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

            case ArchiveFormat.TarGz:
                using (var file = File.OpenRead(archivePath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                using (var tar = new TarReader(gzip))
                {
                    while (tar.GetNextEntry() is { } entry)
                    {
                        if (
                            entry.EntryType
                            is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                        )
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

public sealed class FileDownloader(IHttpClientFactory factory) : IFileDownloader
{
    private readonly IHttpClientFactory _factory = factory;

    public async Task DownloadAsync(
        string url,
        string destination,
        IProgress<InstallProgress>? progress,
        CancellationToken ct,
        string? toolName = null
    )
    {
        var http = _factory.CreateClient("bundled");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var file = File.Create(destination);

        var buffer = new byte[81920];
        long totalRead = 0;
        long lastReported = 0;
        while (await stream.ReadAsync(buffer, ct) is > 0 and var read)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            // Throttle progress reports to once per ~1 MiB to keep the log readable.
            if (totalRead - lastReported >= 1_048_576 || totalRead == totalBytes)
            {
                progress?.Report(
                    new InstallProgress(
                        InstallProgress.Prefix(toolName, "Downloading"),
                        totalRead,
                        totalBytes
                    )
                );
                lastReported = totalRead;
            }
        }
    }
}

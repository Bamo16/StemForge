using StemForge.Core.Services;

namespace StemForge.Tests.Fakes;

/// <summary>
/// A no-op <see cref="IFileDownloader"/> for tests that construct services requiring a downloader
/// but never exercise actual HTTP downloads.
/// </summary>
internal sealed class NullFileDownloader : IFileDownloader
{
    public static readonly NullFileDownloader Instance = new();

    public Task DownloadAsync(
        string url,
        string destination,
        IProgress<InstallProgress>? progress,
        CancellationToken ct,
        string? toolName = null
    ) => Task.CompletedTask;
}

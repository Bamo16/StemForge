using Microsoft.Extensions.DependencyInjection;
using TFile = TagLib.File;

namespace StemForge.Tests.Integration;

/// <summary>
/// Networked download integration test. Resolves the real <see cref="IFileDownloader"/> from the
/// shared DI container and downloads a small, license-safe audio file over the network, then reads
/// its metadata back with TagLibSharp. Excluded from the default run via <see cref="IntegrationGate"/>
/// and runs only when <c>STEMFORGE_INTEGRATION=1</c> is set. Because it depends on a live remote
/// host it MAY fail without blocking the default suite, and is kept separate from the separation
/// test so a failure points clearly at the download path.
///
/// Source: "Example.ogg" from Wikimedia Commons, a public-domain Ogg Vorbis clip.
/// https://commons.wikimedia.org/wiki/File:Example.ogg
/// </summary>
public sealed class NetworkedDownloadIntegrationTests : IDisposable
{
    private const string AudioUrl =
        "https://upload.wikimedia.org/wikipedia/commons/c/c8/Example.ogg";

    private readonly string _tempDir;

    /// <summary>Referenced by <c>SkipUnless</c> (resolved on the test class): true only when the
    /// integration env gate is set.</summary>
    public static bool Enabled => IntegrationGate.Enabled;

    public NetworkedDownloadIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sf-net-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [Fact(SkipUnless = nameof(IntegrationGate.Enabled), Skip = IntegrationGate.SkipReason)]
    public async Task Download_FromUrl_WritesFileWithReadableMetadata()
    {
        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var downloader = provider.GetRequiredService<IFileDownloader>();

        var destination = Path.Combine(_tempDir, "example.ogg");

        await downloader.DownloadAsync(
            AudioUrl,
            destination,
            progress: null,
            ct: TestContext.Current.CancellationToken
        );

        // The downloaded file exists and is non-empty.
        Assert.True(File.Exists(destination), $"Download did not land at {destination}");
        Assert.True(new FileInfo(destination).Length > 0, "Downloaded file should be non-empty.");

        // Metadata: TagLibSharp can open the file and reports a plausible audio stream.
        using var file = TFile.Create(destination);
        Assert.True(
            file.Properties.Duration > TimeSpan.Zero,
            "Downloaded audio should report a non-zero duration."
        );
        Assert.True(
            file.Properties.AudioChannels > 0,
            "Downloaded audio should report at least one channel."
        );
    }
}

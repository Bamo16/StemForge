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

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemforge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

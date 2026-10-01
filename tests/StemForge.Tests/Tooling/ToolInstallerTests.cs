using System.Runtime.InteropServices;
using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Tooling;

public sealed class ToolInstallerTests
{
    private static (ToolInstaller installer, FakeProcessRunner fake, AppPaths paths) Build(
        OSKind os = OSKind.Windows,
        Architecture arch = Architecture.X64
    )
    {
        var fake = new FakeProcessRunner();
        var paths = new AppPaths(new AppSettings());
        var platform = new PlatformInfo(os, arch);
        var installer = new ToolInstaller(
            fake,
            paths,
            new BundledFetcher(paths, platform, NullFileDownloader.Instance),
            platform
        );
        return (installer, fake, paths);
    }

    [Fact]
    public async Task InstallAsync_Uv_Windows_RunsPowershellScript()
    {
        var (installer, fake, _) = Build(OSKind.Windows);
        fake.Setup("powershell", "");

        await installer.InstallAsync(
            ToolCatalog.Get(ToolKind.Uv),
            new(),
            ct: TestContext.Current.CancellationToken
        );

        var (Exe, Args) = Assert.Single(fake.Calls);
        Assert.Equal("powershell", Exe);
        Assert.Contains("irm https://astral.sh/uv/install.ps1 | iex", Args);
    }

    [Fact]
    public async Task UpgradeAsync_AudioSeparator_RunsUvToolUpgrade()
    {
        var (installer, fake, paths) = Build(OSKind.Windows);
        fake.Setup(paths.Uv, "");

        await installer.UpgradeAsync(
            ToolCatalog.Get(ToolKind.AudioSeparator),
            ct: TestContext.Current.CancellationToken
        );

        var (Exe, Args) = Assert.Single(fake.Calls);
        Assert.Equal(paths.Uv, Exe);
        Assert.Equal(["tool", "upgrade", "audio-separator"], Args);
    }

    [Fact]
    public async Task UpgradeAsync_BundledTool_Throws()
    {
        var (installer, _, _) = Build();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            installer.UpgradeAsync(
                ToolCatalog.Get(ToolKind.Ffmpeg),
                ct: TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task InstallAsync_Uv_Linux_RunsShellScript()
    {
        var (installer, fake, _) = Build(OSKind.Linux);
        fake.Setup("sh", "");

        await installer.InstallAsync(
            ToolCatalog.Get(ToolKind.Uv),
            new(),
            ct: TestContext.Current.CancellationToken
        );

        var (Exe, Args) = Assert.Single(fake.Calls);
        Assert.Equal("sh", Exe);
        Assert.Contains("curl -LsSf https://astral.sh/uv/install.sh | sh", Args);
    }

    [Theory]
    [InlineData(GpuVariant.Cuda, "audio-separator[gpu]", true)]
    [InlineData(GpuVariant.DirectML, "audio-separator[dml]", false)]
    [InlineData(GpuVariant.Cpu, "audio-separator[cpu]", false)]
    public async Task InstallAsync_AudioSeparator_BuildsVariantArgs(
        GpuVariant variant,
        string expectedPackage,
        bool expectsCudaIndex
    )
    {
        var (installer, fake, paths) = Build(OSKind.Windows);
        fake.Setup(paths.Uv, "");

        await installer.InstallAsync(
            ToolCatalog.Get(ToolKind.AudioSeparator),
            new(variant),
            ct: TestContext.Current.CancellationToken
        );

        var (Exe, Args) = Assert.Single(fake.Calls);
        Assert.Equal(paths.Uv, Exe);

        string[] expected = expectsCudaIndex
            ?
            [
                "tool",
                "install",
                "--python",
                "3.10",
                "--force",
                expectedPackage,
                "--extra-index-url",
                "https://download.pytorch.org/whl/cu121",
            ]
            : ["tool", "install", "--python", "3.10", "--force", expectedPackage];
        Assert.Equal(expected, Args);
    }

    [Fact]
    public async Task InstallAsync_BundledFetch_NoAssetForPlatform_Throws()
    {
        // No tool ships a 32-bit x86 asset, so a BundledFetch must fail before any download.
        var (installer, _, _) = Build(OSKind.Windows, Architecture.X86);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            installer.InstallAsync(
                ToolCatalog.Get(ToolKind.Ffmpeg),
                new(),
                ct: TestContext.Current.CancellationToken
            )
        );
    }
}

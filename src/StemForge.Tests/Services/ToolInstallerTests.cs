using System.Runtime.InteropServices;
using StemForge.Models;
using StemForge.Services;
using StemForge.Tests.Fakes;

namespace StemForge.Tests.Services;

public sealed class ToolInstallerTests
{
    private static (ToolInstaller installer, FakeProcessRunner fake, AppPaths paths) Build(
        OSKind os = OSKind.Windows
    )
    {
        var fake = new FakeProcessRunner();
        var paths = new AppPaths(new AppSettings());
        var platform = new PlatformInfo(os, Architecture.X64);
        var installer = new ToolInstaller(
            fake,
            paths,
            new BundledFetcher(paths, platform, AppInfo.Current),
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

        var call = Assert.Single(fake.Calls);
        Assert.Equal("powershell", call.Exe);
        Assert.Contains("irm https://astral.sh/uv/install.ps1 | iex", call.Args);
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

        var call = Assert.Single(fake.Calls);
        Assert.Equal("sh", call.Exe);
        Assert.Contains("curl -LsSf https://astral.sh/uv/install.sh | sh", call.Args);
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

        var call = Assert.Single(fake.Calls);
        Assert.Equal(paths.Uv, call.Exe);

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
        Assert.Equal(expected, call.Args);
    }

    [Fact]
    public async Task InstallAsync_BundledFetch_NoAssetForPlatform_Throws()
    {
        // ffmpeg only has a Windows x64 asset; on Linux the fetch must fail before any download.
        var (installer, _, _) = Build(OSKind.Linux);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            installer.InstallAsync(
                ToolCatalog.Get(ToolKind.Ffmpeg),
                new(),
                ct: TestContext.Current.CancellationToken
            )
        );
    }
}

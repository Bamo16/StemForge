using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Offline well-formedness checks for the tool catalog as it would resolve on linux-x64. These
/// assert the data is internally consistent (assets present and checksummed, paths land under
/// bin/ with no .exe suffix, per-OS variant sets present) without touching the network, so they
/// run as part of the normal <c>dotnet test</c> suite and gate the cross-platform CI job.
/// </summary>
public sealed class CatalogWellFormednessTests
{
    private static readonly PlatformInfo LinuxX64 = new(OSKind.Linux, Architecture.X64);

    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$");

    public static IEnumerable<object[]> LinuxBundledTools =>
        ToolCatalog
            .All.Where(t => t.InstallStrategy is BundledFetch)
            .Select(t => new object[] { t.Kind });

    [Theory]
    [MemberData(nameof(LinuxBundledTools))]
    public void EveryBundledTool_HasLinuxX64Asset_WithUrlAndValidSha256(ToolKind kind)
    {
        var tool = ToolCatalog.Get(kind);
        var strategy = Assert.IsType<BundledFetch>(tool.InstallStrategy);

        var asset = strategy.AssetFor(LinuxX64);
        Assert.NotNull(asset);
        Assert.False(
            string.IsNullOrWhiteSpace(asset.Url),
            $"{tool.CliName} linux-x64 asset has an empty URL"
        );
        Assert.Matches(Sha256Hex, asset.Sha256);
    }

    [Fact]
    public void BundledTools_AreInstallableOnLinuxX64()
    {
        var bundled = ToolCatalog.All.Where(t => t.InstallStrategy is BundledFetch).ToList();

        Assert.NotEmpty(bundled);
        Assert.All(bundled, t => Assert.True(t.IsInstallableOn(LinuxX64), $"{t.CliName}"));
    }

    [Theory]
    [InlineData(ToolKind.Ffmpeg, "ffmpeg")]
    [InlineData(ToolKind.Ytdlp, "yt-dlp")]
    [InlineData(ToolKind.Deno, "deno")]
    public void BundledBinaryFileName_OnLinux_HasNoExeSuffix(ToolKind kind, string expected)
    {
        var name = ToolCatalog.Get(kind).BundledBinaryFileName(LinuxX64);

        Assert.Equal(expected, name);
        Assert.DoesNotContain(".exe", name, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ToolKind.Uv, "uv")]
    [InlineData(ToolKind.AudioSeparator, "audio-separator")]
    public void AppPaths_UvBinaries_ResolveUnderBin_WithNoExeOnLinux(ToolKind kind, string leaf)
    {
        var paths = new AppPaths(new AppSettings(), LinuxX64);

        // uv installs itself under ~/.local/bin; its tool shims under <tool>/bin on Unix. Both
        // must resolve with a bare leaf name (no .exe) when the platform is pinned to Linux.
        var uvHome = paths.KnownUvPath;
        Assert.EndsWith(Path.Combine("bin", "uv"), uvHome);
        Assert.DoesNotContain(".exe", uvHome, StringComparison.OrdinalIgnoreCase);

        var shim = kind == ToolKind.Uv ? uvHome : paths.UvAudioSeparatorShim;
        Assert.EndsWith(Path.Combine("bin", leaf), shim);
        Assert.DoesNotContain(".exe", shim, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppPaths_AudioSeparatorPython_ResolvesUnderBin_NoExeOnLinux()
    {
        var paths = new AppPaths(new AppSettings(), LinuxX64);

        Assert.EndsWith(Path.Combine("bin", "python"), paths.UvAudioSeparatorPython);
        Assert.DoesNotContain(
            ".exe",
            paths.UvAudioSeparatorPython,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void AudioSeparator_LinuxVariants_AreCudaAndCpu()
    {
        var install = Assert.IsType<UvToolInstall>(
            ToolCatalog.Get(ToolKind.AudioSeparator).InstallStrategy
        );

        var linux = install.VariantsFor(OSKind.Linux).Select(v => v.Variant).ToList();
        Assert.Equal([GpuVariant.Cuda, GpuVariant.Cpu], linux);
    }

    [Fact]
    public void AudioSeparator_MacOsVariants_AreCpuOnly()
    {
        var install = Assert.IsType<UvToolInstall>(
            ToolCatalog.Get(ToolKind.AudioSeparator).InstallStrategy
        );

        var mac = install.VariantsFor(OSKind.MacOS).Select(v => v.Variant).ToList();
        Assert.Equal([GpuVariant.Cpu], mac);
    }
}

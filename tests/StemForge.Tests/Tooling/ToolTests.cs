using System.Runtime.InteropServices;

namespace StemForge.Tests.Tooling;

public sealed class ToolTests
{
    [Theory]
    [InlineData(ToolKind.Uv, "uv 0.4.0 (abc123 2026-01-01)", "0.4.0")]
    [InlineData(ToolKind.AudioSeparator, "audio-separator 0.27.2", "0.27.2")]
    [InlineData(ToolKind.Ffmpeg, "ffmpeg version 9.0 Copyright (c) 2000-2025", "9.0")]
    [InlineData(ToolKind.Ffmpeg, "ffmpeg version N-118000-gabc1234 Copyright", "N-118000-gabc1234")]
    [InlineData(ToolKind.Ytdlp, "2026.08.19\n", "2026.08.19")]
    [InlineData(
        ToolKind.Deno,
        "deno 2.8.0 (stable, release, x86_64-pc-windows-msvc)\nv8 14.0",
        "2.8.0"
    )]
    public void ExtractVersion_RealToolOutput_IsTheBareVersion(
        ToolKind kind,
        string raw,
        string expected
    ) => Assert.Equal(expected, ToolCatalog.Get(kind).ExtractVersion(raw));

    [Theory]
    [InlineData(ToolKind.Uv, "something else entirely\nsecond line", "something else entirely")]
    [InlineData(ToolKind.Deno, "  odd banner  \nmore", "odd banner")]
    [InlineData(
        ToolKind.Ffmpeg,
        "ffmpeg: command banner changed",
        "ffmpeg: command banner changed"
    )]
    public void ExtractVersion_OutputThePatternDoesNotMatch_FallsBackToTheFirstTrimmedLine(
        ToolKind kind,
        string raw,
        string expected
    ) => Assert.Equal(expected, ToolCatalog.Get(kind).ExtractVersion(raw));

    [Theory]
    [InlineData(OSKind.Windows, "ffmpeg.exe")]
    [InlineData(OSKind.Linux, "ffmpeg")]
    [InlineData(OSKind.MacOS, "ffmpeg")]
    public void BundledBinaryFileName_AppendsThePlatformsExecutableSuffix(
        OSKind os,
        string expected
    ) =>
        Assert.Equal(
            expected,
            ToolCatalog
                .Get(ToolKind.Ffmpeg)
                .BundledBinaryFileName(new PlatformInfo(os, Architecture.X64))
        );

    [Fact]
    public void AssetFor_PlatformTheTableDoesNotList_IsNull()
    {
        var fetch = Assert.IsType<BundledFetch>(ToolCatalog.Get(ToolKind.Deno).InstallStrategy);

        Assert.Null(fetch.AssetFor(new PlatformInfo(OSKind.Linux, Architecture.Wasm)));
        Assert.NotNull(fetch.AssetFor(new PlatformInfo(OSKind.Windows, Architecture.X64)));
    }

    [Fact]
    public void VariantsFor_OsWithNoEntry_IsEmptyRatherThanThrowing()
    {
        var install = new UvToolInstall(
            "pkg",
            null,
            new Dictionary<OSKind, IReadOnlyList<ToolVariant>>
            {
                [OSKind.Windows] = [new(GpuVariant.Cpu, "cpu")],
            },
            null
        );

        Assert.Empty(install.VariantsFor(OSKind.MacOS));
        Assert.Equal(GpuVariant.Cpu, Assert.Single(install.VariantsFor(OSKind.Windows)).Variant);
    }

    [Fact]
    public void ToolVariant_ConstructedWithoutExtraArgs_HasNone() =>
        Assert.Empty(new ToolVariant(GpuVariant.Cpu, "cpu").ExtraArgs);
}

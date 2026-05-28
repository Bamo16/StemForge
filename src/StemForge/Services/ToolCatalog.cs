using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// The single declarative registry of every tool StemForge depends on. Closed set, known at
/// compile time. All tool-aware code (detection, install, path resolution, UI rows) reads from
/// here. Adding a tool is a single entry here plus, if it needs a novel install pattern, a new
/// <see cref="InstallStrategy"/> case. See docs/adr/0002-tool-catalog-architecture.md.
/// </summary>
public static class ToolCatalog
{
    private static readonly PlatformInfo WinX64 = new(OSKind.Windows, Architecture.X64);

    // Tools are inlined into this single initializer rather than referenced as named static
    // members: static initializers run in textual order, so a separate `All = [Uv, ...]` that
    // referenced members declared below it would observe their backing fields while still null.
    public static IReadOnlyList<Tool> All { get; } =
    [
        new(
            ToolKind.Uv,
            CliName: "uv",
            VersionArg: "--version",
            VersionPattern: Pattern(@"^uv\s+(\S+)"),
            IsRequired: true,
            new ScriptInstall(
                new Dictionary<OSKind, ShellCommand>
                {
                    [OSKind.Windows] = new(
                        "powershell",
                        [
                            "-ExecutionPolicy",
                            "ByPass",
                            "-c",
                            "irm https://astral.sh/uv/install.ps1 | iex",
                        ]
                    ),
                    [OSKind.Linux] = new(
                        "sh",
                        ["-c", "curl -LsSf https://astral.sh/uv/install.sh | sh"]
                    ),
                    [OSKind.MacOS] = new(
                        "sh",
                        ["-c", "curl -LsSf https://astral.sh/uv/install.sh | sh"]
                    ),
                }
            )
        ),
        new(
            ToolKind.AudioSeparator,
            CliName: "audio-separator",
            VersionArg: "--version",
            VersionPattern: Pattern(@"(\d+(?:\.\d+)+)"),
            IsRequired: true,
            new UvToolInstall(
                Package: "audio-separator",
                PythonVersion: "3.10",
                Variants: new Dictionary<OSKind, IReadOnlyList<ToolVariant>>
                {
                    [OSKind.Windows] =
                    [
                        // PyTorch CUDA wheels are not on PyPI; cu121 requires CUDA 12.1+ drivers.
                        new(
                            GpuVariant.Cuda,
                            PipExtra: "gpu",
                            ExtraArgs:
                            [
                                "--extra-index-url",
                                "https://download.pytorch.org/whl/cu121",
                            ]
                        ),
                        new(GpuVariant.DirectML, "dml"),
                        new(GpuVariant.Cpu, "cpu"),
                    ],
                },
                VariantProbe: new VariantProbe("tools/detect_variant.py")
            )
        ),
        new(
            ToolKind.Ffmpeg,
            CliName: "ffmpeg",
            VersionArg: "-version",
            VersionPattern: Pattern(@"ffmpeg version\s+(\S+)"),
            IsRequired: true,
            new BundledFetch(
                new Dictionary<PlatformInfo, BundledAsset>
                {
                    [WinX64] = new(
                        Url: "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/"
                            + "autobuild-2026-05-26-17-26/"
                            + "ffmpeg-N-124653-g0ac3b00a18-win64-gpl-shared.zip",
                        Sha256: "5ea46ea816a48f48e0d4c2ccf5997b4201bc8bed0be8ef05ccd169dc91d11dee",
                        ExtractMode: ExtractMode.FlattenFromBinSubdir
                    ),
                }
            )
        ),
        // yt-dlp is UvToolInstall today; Layer 4 of the v0.1.1 refactor flips it to BundledFetch.
        new(
            ToolKind.Ytdlp,
            CliName: "yt-dlp",
            VersionArg: "--version",
            VersionPattern: Pattern(@"^\s*(\d+(?:\.\d+)+)"),
            IsRequired: false,
            new UvToolInstall(
                Package: "yt-dlp",
                PythonVersion: null,
                Variants: new Dictionary<OSKind, IReadOnlyList<ToolVariant>>(),
                VariantProbe: null
            )
        ),
        new(
            ToolKind.Deno,
            CliName: "deno",
            VersionArg: "--version",
            VersionPattern: Pattern(@"^deno\s+(\S+)"),
            IsRequired: false,
            new BundledFetch(
                new Dictionary<PlatformInfo, BundledAsset>
                {
                    [WinX64] = new(
                        Url: "https://github.com/denoland/deno/releases/download/v2.8.0/"
                            + "deno-x86_64-pc-windows-msvc.zip",
                        Sha256: "9b98d1f456878c8ac5caa55779a04f2f1f91f8e942d6ef3f887681698f634adf",
                        ExtractMode: ExtractMode.SingleFileAtRoot
                    ),
                }
            )
        ),
    ];

    public static Tool Get(ToolKind kind) => All.First(t => t.Kind == kind);

    /// <summary>Tools that can be installed on the given platform (have a viable strategy there).</summary>
    public static IReadOnlyList<Tool> AvailableFor(PlatformInfo platform) =>
        [.. All.Where(t => t.IsInstallableOn(platform))];

    private static Regex Pattern([StringSyntax("Regex")] string pattern) =>
        new(pattern, RegexOptions.Multiline);
}

internal static class ToolInstallabilityExtensions
{
    /// <summary>
    /// True when the tool has a viable install path on the platform: a script command for the
    /// OS, a bundled asset for the exact platform, or (for uv-tool installs) we trust uv to
    /// resolve the package on any OS it supports.
    /// </summary>
    public static bool IsInstallableOn(this Tool tool, PlatformInfo platform) =>
        tool.InstallStrategy switch
        {
            ScriptInstall s => s.Commands.ContainsKey(platform.Os),
            BundledFetch b => b.AssetFor(platform) is not null,
            UvToolInstall => true,
            _ => false,
        };
}

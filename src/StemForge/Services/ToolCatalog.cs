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
    private static readonly PlatformInfo LinuxX64 = new(OSKind.Linux, Architecture.X64);

    // PyTorch CUDA wheels are not on PyPI; cu121 requires CUDA 12.1+ drivers. Shared by Windows
    // and Linux, which install the identical wheel index. CPU is the universal fallback.
    private static readonly ToolVariant CudaVariant = new(
        GpuVariant.Cuda,
        PipExtra: "gpu",
        ExtraArgs: ["--extra-index-url", "https://download.pytorch.org/whl/cu121"]
    );
    private static readonly ToolVariant CpuVariant = new(GpuVariant.Cpu, "cpu");

    // Tools are inlined into this single initializer rather than referenced as named static
    // members: static initializers run in textual order, so a separate `All = [Uv, ...]` that
    // referenced members declared below it would observe their backing fields while still null.
    public static IReadOnlyList<Tool> All { get; } =
    [
        new(
            ToolKind.Uv,
            CliName: "uv",
            Description: "Python tool installer used to install audio-separator",
            DownloadSize: "~25 MB download",
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
            Description: "stem separation engine",
            DownloadSize: "~250 MB to 2 GB (varies by GPU variant)",
            VersionArg: "--version",
            VersionPattern: Pattern(@"(\d+(?:\.\d+)+)"),
            IsRequired: true,
            new UvToolInstall(
                Package: "audio-separator",
                PythonVersion: "3.10",
                Variants: new Dictionary<OSKind, IReadOnlyList<ToolVariant>>
                {
                    // CUDA on Windows: NVIDIA GPU plus DirectML fallback for other vendors.
                    [OSKind.Windows] = [CudaVariant, new(GpuVariant.DirectML, "dml"), CpuVariant],
                    // CUDA on Linux mirrors Windows (same cu121 wheel index). No DirectML on Linux.
                    [OSKind.Linux] = [CudaVariant, CpuVariant],
                    // macOS: CPU only for now. CoreML/MPS acceleration is deferred.
                    [OSKind.MacOS] = [CpuVariant],
                },
                VariantProbe: new VariantProbe("tools/detect_variant.py")
            )
        ),
        new(
            ToolKind.Ffmpeg,
            CliName: "ffmpeg",
            Description: "required by audio-separator",
            DownloadSize: "~100 MB download",
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
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.FlattenFromBinSubdir
                    ),
                    // Same BtbN build/version as Windows. Nightly autobuilds publish only a
                    // static linux64-gpl tar.xz (no -shared variant), but its bin/ layout matches
                    // so FlattenFromBinSubdir extracts ffmpeg/ffprobe/ffplay identically.
                    [LinuxX64] = new(
                        Url: "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/"
                            + "autobuild-2026-05-26-17-26/"
                            + "ffmpeg-N-124653-g0ac3b00a18-linux64-gpl.tar.xz",
                        Sha256: "89e1f02736132c39bb98392d561362d7747934466ed480a20fe4481c2f71a9e7",
                        Format: ArchiveFormat.TarXz,
                        Layout: BundledLayout.FlattenFromBinSubdir
                    ),
                }
            )
        ),
        // Bundled rather than uv-tool-installed so it does not shadow a user's own yt-dlp on
        // PATH. Self-updates in place via `yt-dlp.exe --update-to <channel>` between releases.
        new(
            ToolKind.Ytdlp,
            CliName: "yt-dlp",
            Description: "enables downloading audio from YouTube URLs",
            DownloadSize: "~17 MB download",
            VersionArg: "--version",
            VersionPattern: Pattern(@"^\s*(\d+(?:\.\d+)+)"),
            IsRequired: false,
            new BundledFetch(
                new Dictionary<PlatformInfo, BundledAsset>
                {
                    [WinX64] = new(
                        Url: "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp.exe",
                        Sha256: "3db811b366b2da47337d2fcfdfe5bbd9a258dad3f350c54974f005df115a1545",
                        Format: ArchiveFormat.RawBinary,
                        Layout: BundledLayout.DownloadIsBinary
                    ),
                    [LinuxX64] = new(
                        Url: "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp_linux",
                        Sha256: "c2b0189f581fe4a2ddd41954f1bcb7d327db04b07ed0dea97e4f1b3e09b5dd8e",
                        Format: ArchiveFormat.RawBinary,
                        Layout: BundledLayout.DownloadIsBinary
                    ),
                }
            )
        ),
        new(
            ToolKind.Deno,
            CliName: "deno",
            Description: "JS runtime yt-dlp uses to solve YouTube challenges",
            DownloadSize: "~42 MB download",
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
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.SingleFileAtRoot
                    ),
                    [LinuxX64] = new(
                        Url: "https://github.com/denoland/deno/releases/download/v2.8.0/"
                            + "deno-x86_64-unknown-linux-gnu.zip",
                        Sha256: "be2c8b53c8ca1d66be76feb9b1a524419da708b00d4ca074cf5c633c81c1627b",
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.SingleFileAtRoot
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

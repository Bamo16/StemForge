using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace StemForge.Core.Tooling;

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

    // macOS support is best-effort: assets are pinned and SHA-verified, but not yet run-verified
    // on real hardware (no macOS tester). Apple Silicon (Arm64) is the primary target; X64 is
    // covered where the asset is cheap. See issue #20.
    private static readonly PlatformInfo MacArm64 = new(OSKind.MacOS, Architecture.Arm64);
    private static readonly PlatformInfo MacX64 = new(OSKind.MacOS, Architecture.X64);

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
                    // FFmpeg-Builds retains exactly one build per calendar month (the last-day
                    // autobuild-YYYY-MM-DD tag). Daily builds are pruned after ~2 weeks. Always
                    // pin to a month-end tag here; mid-month tags will 404 within weeks.
                    [WinX64] = new(
                        Url: "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/"
                            + "autobuild-2026-05-31-15-28/"
                            + "ffmpeg-N-124716-g054dffd133-win64-gpl-shared.zip",
                        Sha256: "1718fdeaaade345f92115319e0852cfd78551c67f24bc5deff76ab4fd1d85faa",
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.FlattenFromBinSubdir
                    ),
                    // Same BtbN build/version as Windows. Nightly autobuilds publish only a
                    // static linux64-gpl tar.xz (no -shared variant), but its bin/ layout matches
                    // so FlattenFromBinSubdir extracts ffmpeg/ffprobe/ffplay identically.
                    [LinuxX64] = new(
                        Url: "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/"
                            + "autobuild-2026-05-31-15-28/"
                            + "ffmpeg-N-124716-g054dffd133-linux64-gpl.tar.xz",
                        Sha256: "64b6f4b1e68c54f6c0bc90d0f0f684dd8c3f68e95fe817ef8849d8a0d6a81c59",
                        Format: ArchiveFormat.TarXz,
                        Layout: BundledLayout.FlattenFromBinSubdir
                    ),
                    // macOS (best-effort, unverified at runtime). evermeet.cx ships a single
                    // x86_64 ffmpeg binary at the zip root (no bin/ subdir, no bundled ffprobe).
                    // It runs natively on Intel and under Rosetta 2 on Apple Silicon, so the same
                    // asset backs both arches. SingleFileAtRoot, not FlattenFromBinSubdir.
                    [MacArm64] = new(
                        Url: "https://evermeet.cx/ffmpeg/ffmpeg-8.1.1.zip",
                        Sha256: "4610988e2f54c243c50da73a09e4e2c36d9bb77546f9aa6c84cb328dcb1a98c1",
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.SingleFileAtRoot
                    ),
                    [MacX64] = new(
                        Url: "https://evermeet.cx/ffmpeg/ffmpeg-8.1.1.zip",
                        Sha256: "4610988e2f54c243c50da73a09e4e2c36d9bb77546f9aa6c84cb328dcb1a98c1",
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.SingleFileAtRoot
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
                    // macOS (best-effort, unverified at runtime). yt-dlp_macos is a universal
                    // Mach-O binary (x86_64 + arm64), so the one asset backs both arches.
                    [MacArm64] = new(
                        Url: "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp_macos",
                        Sha256: "e80c47b3ce712acee51d5e3d4eace2d181b44d38f1942c3a32e3c7ff53cd9ed5",
                        Format: ArchiveFormat.RawBinary,
                        Layout: BundledLayout.DownloadIsBinary
                    ),
                    [MacX64] = new(
                        Url: "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp_macos",
                        Sha256: "e80c47b3ce712acee51d5e3d4eace2d181b44d38f1942c3a32e3c7ff53cd9ed5",
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
                    // macOS (best-effort, unverified at runtime). Separate per-arch zips, each a
                    // single deno binary at the root. Version matches the Windows/Linux v2.8.0 pin.
                    [MacArm64] = new(
                        Url: "https://github.com/denoland/deno/releases/download/v2.8.0/"
                            + "deno-aarch64-apple-darwin.zip",
                        Sha256: "dba813b8b69d6218cffb11252b9e4e6036ca2c9d79843cde367b4b369aaf9634",
                        Format: ArchiveFormat.Zip,
                        Layout: BundledLayout.SingleFileAtRoot
                    ),
                    [MacX64] = new(
                        Url: "https://github.com/denoland/deno/releases/download/v2.8.0/"
                            + "deno-x86_64-apple-darwin.zip",
                        Sha256: "d6eb643b7f1afb22139f4aa17c4d97bf7ddab4e01e1820edcb30b9ae5c3a7391",
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

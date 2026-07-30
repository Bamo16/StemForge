namespace StemForge.Core.Tooling;

/// <summary>
/// How a <see cref="Tool"/> reaches the user's disk. Sealed three-case hierarchy: the install
/// orchestrator switches on the concrete type. Strategy is fixed per tool; cross-platform
/// variation lives as data inside the strategy (per-OS commands, per-platform asset table),
/// never as strategy substitution. See docs/adr/0002-tool-catalog-architecture.md.
/// </summary>
public abstract record InstallStrategy;

/// <summary>
/// Runs an upstream installer command. Used only by uv. The command differs per OS (PowerShell
/// + irm on Windows, sh + curl on Unix). The installed binary lands on the user's system PATH
/// via the upstream installer's own behaviour.
/// </summary>
public sealed record ScriptInstall(IReadOnlyDictionary<OSKind, ShellCommand> Commands)
    : InstallStrategy;

/// <summary>
/// Installs a Python package via <c>uv tool install</c>. Used only by audio-separator. The
/// result lives in uv's shim directory, which is on the user's PATH. May carry per-platform
/// install <see cref="Variants"/> (pip extras + extra args) and a <see cref="VariantProbe"/>
/// for detecting which variant is actually functional after install.
/// </summary>
public sealed record UvToolInstall(
    string Package,
    string? PythonVersion,
    IReadOnlyDictionary<OSKind, IReadOnlyList<ToolVariant>> Variants,
    VariantProbe? VariantProbe
) : InstallStrategy
{
    public IReadOnlyList<ToolVariant> VariantsFor(OSKind os) =>
        Variants.GetValueOrDefault(os) ?? [];
}

/// <summary>
/// Downloads a pinned archive into <see cref="AppPaths.BundledBinDir"/> and verifies its
/// SHA-256. Used by yt-dlp, ffmpeg, deno. The binary is passed to child processes via
/// explicit tool args rather than PATH injection.
/// </summary>
public sealed record BundledFetch(IReadOnlyDictionary<PlatformInfo, BundledAsset> Assets)
    : InstallStrategy
{
    public BundledAsset? AssetFor(PlatformInfo platform) => Assets.GetValueOrDefault(platform);
}

/// <summary>A shell command: the executable plus its argument list.</summary>
public sealed record ShellCommand(string Executable, IReadOnlyList<string> Arguments);

/// <summary>
/// A user-selectable install configuration within <see cref="UvToolInstall"/>. Encodes the pip
/// extra and any extra install arguments (e.g. the PyTorch CUDA wheel index URL).
/// </summary>
public sealed record ToolVariant(
    GpuVariant Variant,
    string PipExtra,
    IReadOnlyList<string> ExtraArgs
)
{
    /// <summary>A variant with no extra install arguments.</summary>
    public ToolVariant(GpuVariant variant, string pipExtra)
        : this(variant, pipExtra, []) { }
}

/// <summary>
/// Locates the one-shot Python script that probes which <see cref="GpuVariant"/> is actually
/// functional in an installed environment. The script path is relative to the app base
/// directory; the install orchestrator supplies the interpreter from the tool's uv environment.
/// </summary>
public sealed record VariantProbe(string ScriptRelativePath);

/// <summary>
/// A pinned downloadable asset for one platform. Extraction is described by two orthogonal axes:
/// the <see cref="Format"/> of the download (how to decompress it) and the <see cref="Layout"/>
/// (where the target binary lives inside it). This avoids combinatorial enum cases like a
/// "tar.xz-flatten-from-bin" mode. See docs/adr/0005-bundle-ffmpeg-everywhere-via-tar-xz.md.
/// </summary>
public sealed record BundledAsset(
    string Url,
    string Sha256,
    ArchiveFormat Format,
    BundledLayout Layout
);

/// <summary>The container format of a downloaded <see cref="BundledAsset"/>.</summary>
public enum ArchiveFormat
{
    /// <summary>Not an archive: the download is the binary itself (e.g. yt-dlp.exe).</summary>
    RawBinary,

    /// <summary>A zip archive (e.g. deno, Windows ffmpeg).</summary>
    Zip,

    /// <summary>An xz-compressed tar archive (e.g. Linux ffmpeg). Decoded via SharpCompress.</summary>
    TarXz,
}

/// <summary>Where the target binary lives inside a downloaded <see cref="BundledAsset"/>.</summary>
public enum BundledLayout
{
    /// <summary>
    /// The download itself is the binary; nothing to extract. Only valid with
    /// <see cref="ArchiveFormat.RawBinary"/>.
    /// </summary>
    DownloadIsBinary,

    /// <summary>The target binary sits at the archive root (e.g. deno).</summary>
    SingleFileAtRoot,

    /// <summary>
    /// Flatten every file under any <c>/bin/</c> subpath into the bundle dir (e.g. ffmpeg shared
    /// builds, whose runtime DLLs/.so files live alongside the exe under <c>bin/</c>).
    /// </summary>
    FlattenFromBinSubdir,
}

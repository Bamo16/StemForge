using StemForge.Models;

namespace StemForge.Services;

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
/// SHA-256. Used by yt-dlp, ffmpeg, deno. The binary is not added to the user's system PATH;
/// it is reachable from StemForge child processes only (ProcessRunner prepends BundledBinDir).
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

/// <summary>A pinned downloadable asset for one platform.</summary>
public sealed record BundledAsset(string Url, string Sha256, ExtractMode ExtractMode);

/// <summary>How to turn a downloaded asset into the bundled binary.</summary>
public enum ExtractMode
{
    /// <summary>The download is the binary itself, not an archive (e.g. yt-dlp.exe).</summary>
    RawBinary,

    /// <summary>Archive contains the target exe at its root (e.g. deno).</summary>
    SingleFileAtRoot,

    /// <summary>Flatten every file under any <c>/bin/</c> subpath into the bundle dir (e.g. ffmpeg shared build).</summary>
    FlattenFromBinSubdir,
}

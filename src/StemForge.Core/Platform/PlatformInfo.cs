using System.Runtime.InteropServices;

namespace StemForge.Core.Platform;

public enum OSKind
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>
/// Identifies the runtime OS and CPU architecture. Used as the key into a tool's per-platform
/// asset table so install logic stays data-driven rather than branching on
/// <see cref="RuntimeInformation"/> at every call site. Injected as a singleton (defaulting to
/// <see cref="Current"/>) so tests can pin a platform without process-isolation gymnastics.
/// </summary>
public sealed record PlatformInfo(OSKind Os, Architecture Arch)
{
    public static PlatformInfo Current { get; } =
        new(DetectOs(), RuntimeInformation.ProcessArchitecture);

    public string ExecutableSuffix => Os == OSKind.Windows ? ".exe" : "";

    private static OSKind DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OSKind.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return OSKind.MacOS;
        return OSKind.Linux;
    }
}

using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

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
        new(DetectOs(), RuntimeInformation.ProcessArchitecture)
        {
            LongPathsEnabled = DetectLongPaths(),
        };

    /// <summary>
    /// False on Windows unless long paths are turned on, when tools such as Python cannot write a
    /// path past MAX_PATH. Other systems have no such limit.
    /// </summary>
    public bool LongPathsEnabled { get; init; } = true;

    public string ExecutableSuffix => Os == OSKind.Windows ? ".exe" : "";

    private static bool DetectLongPaths()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        try
        {
            return Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem",
                "LongPathsEnabled",
                0
            )
                is 1;
        }
        catch (Exception ex) when (ex is SecurityException or IOException)
        {
            return false;
        }
    }

    private static OSKind DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OSKind.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return OSKind.MacOS;
        return OSKind.Linux;
    }
}

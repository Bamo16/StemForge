using System.Runtime.InteropServices;
using StemForge.Extensions;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.Tests.Services;

public sealed class AppPathsTests
{
    private static AppPaths Build(OSKind os) =>
        new(new AppSettings(), new PlatformInfo(os, Architecture.X64));

    private static string UserProfile(params string[] segments) =>
        Environment.SpecialFolder.UserProfile.GetFolderPath(segments);

    private static string AppData(params string[] segments) =>
        Environment.SpecialFolder.ApplicationData.GetFolderPath(segments);

    // ── Known uv install location ──────────────────────────────────────────────

    [Fact]
    public void KnownUvPath_Windows_UsesExeExtension()
    {
        var paths = Build(OSKind.Windows);

        Assert.Equal(UserProfile(".local", "bin", "uv.exe"), paths.KnownUvPath);
    }

    [Theory]
    [InlineData(OSKind.Linux)]
    [InlineData(OSKind.MacOS)]
    public void KnownUvPath_Unix_HasNoExtension(OSKind os)
    {
        var paths = Build(os);

        Assert.Equal(UserProfile(".local", "bin", "uv"), paths.KnownUvPath);
    }

    // ── uv-installed audio-separator shim ───────────────────────────────────────

    [Fact]
    public void UvAudioSeparatorShim_Windows_UsesScriptsAndExe()
    {
        var paths = Build(OSKind.Windows);

        Assert.Equal(
            AppData("uv", "tools", "audio-separator", "Scripts", "audio-separator.exe"),
            paths.UvAudioSeparatorShim
        );
    }

    [Theory]
    [InlineData(OSKind.Linux)]
    [InlineData(OSKind.MacOS)]
    public void UvAudioSeparatorShim_Unix_UsesBinAndNoExtension(OSKind os)
    {
        var paths = Build(os);

        Assert.Equal(
            AppData("uv", "tools", "audio-separator", "bin", "audio-separator"),
            paths.UvAudioSeparatorShim
        );
    }

    // ── uv-installed python interpreter ─────────────────────────────────────────

    [Fact]
    public void UvAudioSeparatorPython_Windows_UsesScriptsAndExe()
    {
        var paths = Build(OSKind.Windows);

        Assert.Equal(
            AppData("uv", "tools", "audio-separator", "Scripts", "python.exe"),
            paths.UvAudioSeparatorPython
        );
    }

    [Theory]
    [InlineData(OSKind.Linux)]
    [InlineData(OSKind.MacOS)]
    public void UvAudioSeparatorPython_Unix_UsesBinAndNoExtension(OSKind os)
    {
        var paths = Build(os);

        Assert.Equal(
            AppData("uv", "tools", "audio-separator", "bin", "python"),
            paths.UvAudioSeparatorPython
        );
    }
}

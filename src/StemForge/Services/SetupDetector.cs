using StemForge.Extensions;
using StemForge.Models;

namespace StemForge.Services;

public sealed record ToolInfo(string Name, bool Found, string? Version, bool IsRequired);

public static class SetupDetector
{
    /// <summary>
    /// Known path where uv installs the audio-separator shim on Windows.
    /// Falls back to PATH lookup.
    /// </summary>
    public static string ResolveAudioSeparatorPath()
    {
        var uvShim = Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "uv",
            "tools",
            "audio-separator",
            "Scripts",
            "audio-separator.exe"
        );
        return File.Exists(uvShim) ? uvShim : "audio-separator";
    }

    public static Task<IReadOnlyList<ToolInfo>> DetectAllAsync(string? ytdlpPath) =>
        Task.Run(async () =>
        {
            var results = await Task.WhenAll(
                DetectAsync("uv", "uv", "--version", required: true),
                DetectAsync(
                    "audio-separator",
                    ResolveAudioSeparatorPath(),
                    "--version",
                    required: true
                ),
                DetectAsync("yt-dlp", ytdlpPath ?? "yt-dlp", "--version", required: false),
                DetectAsync("ffmpeg", "ffmpeg", "-version", required: false)
            );
            return (IReadOnlyList<ToolInfo>)results;
        });

    private static async Task<ToolInfo> DetectAsync(
        string name,
        string exe,
        string versionArg,
        bool required
    )
    {
        try
        {
            var r = await ProcessRunner.RunAsync(exe, [versionArg]);
            var version = r.Output.Split('\n')[0].Trim();
            return new ToolInfo(name, Found: true, Version: version, IsRequired: required);
        }
        catch
        {
            return new ToolInfo(name, Found: false, Version: null, IsRequired: required);
        }
    }

    /// <summary>Maps GpuVariant to the audio-separator pip extras name.</summary>
    public static string GetPipExtra(GpuVariant variant) =>
        variant switch
        {
            GpuVariant.Cuda => "gpu",
            GpuVariant.DirectML => "dml",
            _ => "cpu",
        };
}

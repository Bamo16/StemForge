using System.Diagnostics;
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
        var uvShim = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uv", "tools", "audio-separator", "Scripts", "audio-separator.exe"
        );
        return File.Exists(uvShim) ? uvShim : "audio-separator";
    }

    public static async Task<IReadOnlyList<ToolInfo>> DetectAllAsync(
        string? ytdlpPath,
        string? ffmpegPath
    )
    {
        var audioSep = await DetectAsync("audio-separator", ResolveAudioSeparatorPath(), "--version", required: true);
        var ytdlp    = await DetectAsync("yt-dlp",          ytdlpPath ?? "yt-dlp",       "--version", required: false);
        var ffmpeg   = await DetectAsync("ffmpeg",          ffmpegPath ?? "ffmpeg",       "-version",  required: false);
        return [audioSep, ytdlp, ffmpeg];
    }

    private static async Task<ToolInfo> DetectAsync(
        string name, string exe, string versionArg, bool required)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, versionArg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var version = stdout.Split('\n')[0].Trim();
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
            GpuVariant.Cuda     => "gpu",
            GpuVariant.DirectML => "dml",
            _                   => "cpu",
        };
}

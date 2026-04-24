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

    public static Task<IReadOnlyList<ToolInfo>> DetectAllAsync(
        string? ytdlpPath,
        string? ffmpegPath
    ) => Task.Run(async () =>
    {
        // Run all three detections in parallel — each spawns a short-lived process.
        var results = await Task.WhenAll(
            DetectAsync("audio-separator", ResolveAudioSeparatorPath(), "--version", required: true),
            DetectAsync("yt-dlp",          ytdlpPath  ?? "yt-dlp",     "--version", required: false),
            DetectAsync("ffmpeg",          ffmpegPath ?? "ffmpeg",      "-version",  required: false)
        );
        return (IReadOnlyList<ToolInfo>)results;
    });

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
            // Read both streams concurrently — not draining stderr causes a deadlock
            // if the process writes enough to fill the OS pipe buffer.
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask, p.WaitForExitAsync());
            // Some tools (e.g. audio-separator) print version to stderr via Python logging
            var output = (await stdoutTask).Trim();
            if (string.IsNullOrEmpty(output))
                output = (await stderrTask).Trim();
            var version = output.Split('\n')[0].Trim();
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

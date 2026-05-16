using StemForge.Models;

namespace StemForge.Services;

public sealed record ToolInfo(string Name, bool Found, string? Version, bool IsRequired);

public sealed class SetupDetector(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;

    public Task<IReadOnlyList<ToolInfo>> DetectAllAsync() =>
        Task.Run(async () =>
        {
            var results = await Task.WhenAll(
                DetectAsync("uv", _paths.Uv, "--version", required: true),
                DetectAsync("audio-separator", _paths.AudioSeparator, "--version", required: true),
                DetectAsync("yt-dlp", _paths.Ytdlp, "--version", required: false),
                DetectAsync("ffmpeg", _paths.Ffmpeg, "-version", required: false)
            );
            return (IReadOnlyList<ToolInfo>)results;
        });

    private async Task<ToolInfo> DetectAsync(
        string name,
        string exe,
        string versionArg,
        bool required
    )
    {
        try
        {
            var r = await _runner.RunAsync(exe, [versionArg]);
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

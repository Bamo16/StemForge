using StemForge.Helpers;
using StemForge.Models;

namespace StemForge.Services;

public sealed record ToolInfo(string Name, bool Found, string? Version, bool IsRequired);

public sealed class SetupDetector(IProcessRunner runner, AppPaths paths)
{
    // Ordered required-first, then optional. The wizard install step renders rows in this
    // sequence so required tools are visually grouped at the top.
    private static readonly string[] AllToolNames =
    [
        "uv",
        "audio-separator",
        "ffmpeg",
        "yt-dlp",
        "deno",
    ];

    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;

    public Task<IReadOnlyList<ToolInfo>> DetectAllAsync() => DetectAsync(AllToolNames);

    public Task<IReadOnlyList<ToolInfo>> DetectAsync(params string[] toolNames) =>
        Task.Run(async () =>
        {
            var results = await Task.WhenAll(toolNames.Select(DetectOneAsync));
            return (IReadOnlyList<ToolInfo>)results;
        });

    private Task<ToolInfo> DetectOneAsync(string name) =>
        name switch
        {
            "uv" => DetectAsync("uv", _paths.Uv, "--version", required: true),
            "audio-separator" => DetectAsync(
                "audio-separator",
                _paths.AudioSeparator,
                "--version",
                required: true
            ),
            "yt-dlp" => DetectAsync("yt-dlp", _paths.Ytdlp, "--version", required: false),
            "ffmpeg" => DetectAsync("ffmpeg", _paths.Ffmpeg, "-version", required: true),
            "deno" => DetectAsync("deno", _paths.Deno, "--version", required: false),
            _ => throw new ArgumentException($"Unknown tool: {name}", nameof(name)),
        };

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
            var version = ToolVersionExtractor.Extract(name, r.Output);
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

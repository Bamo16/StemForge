using StemForge.Models;

namespace StemForge.Services;

public sealed record ToolInfo(
    ToolKind Kind,
    string Name,
    bool Found,
    string? Version,
    bool IsRequired
);

public sealed class SetupDetector(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;

    public Task<IReadOnlyList<ToolInfo>> DetectAllAsync() => Detect(ToolCatalog.All);

    /// <summary>
    /// Detect a subset of tools by CLI name. Names that don't match a catalog entry are ignored.
    /// </summary>
    public Task<IReadOnlyList<ToolInfo>> DetectAsync(params string[] toolNames) =>
        Detect([.. ToolCatalog.All.Where(t => toolNames.Contains(t.CliName))]);

    private Task<IReadOnlyList<ToolInfo>> Detect(IReadOnlyList<Tool> tools) =>
        Task.Run(async () =>
        {
            var results = await Task.WhenAll(tools.Select(DetectOneAsync));
            return (IReadOnlyList<ToolInfo>)results;
        });

    private async Task<ToolInfo> DetectOneAsync(Tool tool)
    {
        try
        {
            var r = await _runner.RunAsync(_paths.PathFor(tool.Kind), [tool.VersionArg]);
            return new ToolInfo(
                tool.Kind,
                tool.CliName,
                Found: true,
                Version: tool.ExtractVersion(r.Output),
                tool.IsRequired
            );
        }
        catch
        {
            return new ToolInfo(
                tool.Kind,
                tool.CliName,
                Found: false,
                Version: null,
                tool.IsRequired
            );
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

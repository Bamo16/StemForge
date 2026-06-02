using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Runtime detection state for one catalog <see cref="Tool"/>: the immutable descriptor plus the
/// genuinely-runtime <see cref="Found"/>/<see cref="Version"/>. Name and IsRequired are NOT copied;
/// they are read straight off the referenced <see cref="Tool"/> so the catalog stays the single
/// source of per-tool metadata.
/// </summary>
public sealed record ToolState(Tool Tool, bool Found, string? Version)
{
    public ToolKind Kind => Tool.Kind;
    public string Name => Tool.CliName;
    public bool IsRequired => Tool.IsRequired;
}

public sealed class SetupDetector(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;

    public Task<IReadOnlyList<ToolState>> DetectAllAsync() => Detect(ToolCatalog.All);

    /// <summary>
    /// Detect a subset of tools by <see cref="ToolKind"/>. Kinds not in the catalog are ignored.
    /// </summary>
    public Task<IReadOnlyList<ToolState>> DetectAsync(params ToolKind[] kinds) =>
        Detect([.. ToolCatalog.All.Where(t => kinds.Contains(t.Kind))]);

    private Task<IReadOnlyList<ToolState>> Detect(IReadOnlyList<Tool> tools) =>
        Task.Run(async () =>
        {
            var results = await Task.WhenAll(tools.Select(DetectOneAsync));
            return (IReadOnlyList<ToolState>)results;
        });

    private async Task<ToolState> DetectOneAsync(Tool tool)
    {
        try
        {
            var r = await _runner.RunAsync(_paths.PathFor(tool.Kind), [tool.VersionArg]);
            return new ToolState(tool, Found: true, Version: tool.ExtractVersion(r.Output));
        }
        catch
        {
            return new ToolState(tool, Found: false, Version: null);
        }
    }
}

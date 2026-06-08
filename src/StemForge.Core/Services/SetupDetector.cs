using StemForge.Core.Models;

namespace StemForge.Core.Services;

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

    /// <summary>
    /// Detect tool availability. Pass no arguments to detect all catalog tools; pass one or more
    /// <see cref="ToolKind"/>s to detect only that subset. Kinds not in the catalog are ignored.
    /// </summary>
    public async Task<IReadOnlyList<ToolState>> DetectAsync(params ToolKind[] kinds) =>
        await Task.WhenAll(
            (
                kinds is { Length: > 0 }
                    ? ToolCatalog.All.Where(t => kinds.Contains(t.Kind))
                    : ToolCatalog.All
            ).Select(DetectOneAsync)
        );

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

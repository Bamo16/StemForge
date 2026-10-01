namespace StemForge.ViewModels;

public sealed class ToolStatusViewModel(ToolState info, string? variantTag = null)
{
    public string Name { get; } = info.Name;
    public bool Found { get; } = info.Found;
    public string Version { get; } = info.Version ?? string.Empty;
    public bool IsRequired { get; } = info.IsRequired;
    public string StatusLine =>
        Found ? Version : (IsRequired ? "Not found" : "Not found (optional)");
    public string? VariantTag { get; } = variantTag;
}

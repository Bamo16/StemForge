using StemForge.Core.Services;

namespace StemForge.ViewModels;

public sealed class ToolStatusViewModel
{
    public string Name { get; }
    public bool Found { get; }
    public string Version { get; }
    public bool IsRequired { get; }
    public string StatusLine =>
        Found ? Version : (IsRequired ? "Not found" : "Not found (optional)");
    public string? VariantTag { get; }

    public ToolStatusViewModel(ToolState info, string? variantTag = null)
    {
        Name = info.Name;
        Found = info.Found;
        Version = info.Version ?? string.Empty;
        IsRequired = info.IsRequired;
        VariantTag = variantTag;
    }
}

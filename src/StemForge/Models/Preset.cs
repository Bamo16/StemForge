namespace StemForge.Models;

public enum PresetCategory
{
    Vocals,
    Instrumentals,
    Other,
}

public sealed record Preset(
    string Id,
    string Label,
    PresetCategory Category,
    string Description,
    int ModelCount,
    string Vram);

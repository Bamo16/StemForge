namespace StemForge.Models;

/// <summary>Immutable definition of a separation job — what to run and where to put the output.</summary>
public sealed record JobRecord(
    Guid Id,
    string InputFilePath,
    IReadOnlyList<Preset> Presets,
    string OutputDir,
    string ModelsDir
)
{
    public string InputFileName => Path.GetFileName(InputFilePath);
    public string PresetSummary =>
        Presets.Count == 1 ? Presets[0].Label : $"{Presets.Count} presets";
}

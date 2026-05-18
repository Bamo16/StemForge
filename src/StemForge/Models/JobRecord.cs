namespace StemForge.Models;

/// <summary>Immutable definition of a separation job — what to run and where to put the output.</summary>
public sealed record JobRecord(
    Guid Id,
    string? InputFilePath,
    string? SourceUrl,
    IReadOnlyList<Preset> Presets,
    string OutputDir,
    string ModelsDir,
    AudioFormat StemOutputFormat = AudioFormat.Flac,
    bool KeepSourceFile = false
)
{
    public string InputFileName =>
        InputFilePath is not null ? Path.GetFileName(InputFilePath) : SourceUrl ?? string.Empty;

    public string PresetSummary =>
        Presets.Count == 1 ? Presets[0].Label : $"{Presets.Count} presets";
}

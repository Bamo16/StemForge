using Humanizer;

namespace StemForge.Core.Separation.Models;

/// <summary>Immutable definition of a separation job — what to run and where to put the output.</summary>
public sealed record JobRecord(
    Guid Id,
    string? InputFilePath,
    string? SourceUrl,
    IReadOnlyList<Preset> Presets,
    string OutputDir,
    string ModelsDir,
    AudioFormat StemOutputFormat = AudioFormat.Flac,
    bool KeepSourceFile = false,
    YtDlpMetadata? PreResolvedMeta = null,
    bool ExtractDrums = false
)
{
    public string InputFileName =>
        PreResolvedMeta?.Title
        ?? (
            InputFilePath is not null ? Path.GetFileName(InputFilePath) : SourceUrl ?? string.Empty
        );

    public string PresetSummary =>
        ExtractDrums ? $"{"preset".ToQuantity(Presets.Count)} + Drums"
        : Presets.Count == 1 ? Presets[0].Label
        : "preset".ToQuantity(Presets.Count);
}

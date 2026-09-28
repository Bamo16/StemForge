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

    /// <summary>True when the job only fetches the source: no preset and no drum stem.</summary>
    public bool IsSourceOnly =>
        this
            is {
                Presets.Count: 0,
                ExtractDrums: false,
                KeepSourceFile: true,
                SourceUrl.Length: > 0
            };

    /// <summary>What the job writes, for the queue: "Balanced + Source", "2 presets + Drums", "Drums only".</summary>
    public string OutputSummary
    {
        get
        {
            List<string> parts = Presets.Count switch
            {
                0 => [],
                1 => [Presets[0].Label],
                var count => ["preset".ToQuantity(count)],
            };
            if (ExtractDrums)
                parts.Add("Drums");
            if (KeepSourceFile && SourceUrl is not null)
                parts.Add("Source");

            return parts switch
            {
                [var only] when Presets.Count == 0 => $"{only} only",
                _ => string.Join(" + ", parts),
            };
        }
    }
}

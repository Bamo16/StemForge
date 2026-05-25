namespace StemForge.Models;

/// <summary>
/// Describes a single separation run sent to the driver as a <c>run</c> command.
/// Exactly one of <see cref="PresetId"/> or <see cref="Models"/> must be non-null.
/// </summary>
public sealed record JobRequest(
    string AudioPath,
    string OutputDir,
    string OutputFormat,
    string? PresetId,
    IReadOnlyList<string>? Models,
    string? Algorithm,
    IReadOnlyList<double>? Weights = null,
    IReadOnlyList<string>? StemsToKeep = null,
    IReadOnlyDictionary<string, string>? CustomOutputNames = null
);

using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Core.Catalog;

/// <summary>
/// Lists supported separation models and parses the result. Runs the lightweight
/// list_models.py one-shot (via <see cref="LightweightCatalog"/>), which reads the model registry
/// from audio_separator's static data files (models.json, models-scores.json) plus the cached
/// remote download list, WITHOUT importing the audio_separator package (which would pull in torch
/// at module load even though no inference happens). Results are cached until Invalidate() or a
/// forced refresh.
/// </summary>
public sealed class ModelCatalogService(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;
    private IReadOnlyList<ModelInfo>? _cache;

    public void Invalidate() => _cache = null;

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        bool forceRefresh = false,
        CancellationToken ct = default
    )
    {
        if (!forceRefresh && _cache is not null)
            return _cache;

        // The script reads download_checks.json from the models directory and merges it with the
        // bundled lists; pass the directory so the live UVR catalog is included.
        var raw = await LightweightCatalog.RunScriptAsync(
            _runner,
            _paths,
            AppPaths.ListModelsScript,
            [_paths.ModelsDirectory],
            "ModelCatalog",
            ct
        );

        _cache = ParseModels(raw);
        return _cache;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    internal static IReadOnlyList<ModelInfo> ParseModels(string? raw)
    {
        // The output is a nested object: { "Arch": { "Friendly Name": { ...model... } } }.
        var json = LightweightCatalog.ExtractJsonObject(raw);
        if (json is null)
            return [];

        Dictionary<string, Dictionary<string, ModelEntryDto>>? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize(
                json,
                ModelCatalogJsonContext.Default.ModelCatalog
            );
        }
        catch (Exception ex)
        {
            AppLogger.Warning("ModelCatalog", $"Failed to parse model catalog: {ex.Message}");
            return [];
        }

        if (catalog is null)
            return [];

        var list = new List<ModelInfo>();

        // Upstream output can list the same filename more than once (cache-dependent). filename is
        // the canonical model identity, so dedupe on it: first occurrence wins.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Top-level keys are architecture names ("MDX", "Demucs", "MDXC", "VR Arch"); second-level
        // keys are the human-readable model names.
        foreach (var (arch, models) in catalog)
        {
            foreach (var (friendlyName, entry) in models)
            {
                var filename = entry.Filename;
                if (string.IsNullOrWhiteSpace(filename) || !seen.Add(filename))
                    continue;

                list.Add(new ModelInfo(filename, arch, friendlyName, MapStems(entry)));
            }
        }

        return list;
    }

    /// <summary>Pairs each stem name with its SDR from the entry's score map (null when absent).</summary>
    private static IReadOnlyList<StemSdr> MapStems(ModelEntryDto entry) =>
        [.. entry.Stems.Select(stem => new StemSdr(stem, SdrFor(entry.Scores, stem)))];

    /// <summary>
    /// Reads a stem's SDR from the entry's score map. Upstream mixes per-stem score objects with
    /// scalar metrics (e.g. "seconds_per_minute_m3") under the same map, so only an object value
    /// carrying a numeric "SDR" yields a score; anything else (scalar, missing, non-numeric) is
    /// null. This keeps one unexpected field from breaking the whole catalog parse.
    /// </summary>
    internal static double? SdrFor(Dictionary<string, JsonElement>? scores, string stem) =>
        scores is not null
        && scores.TryGetValue(stem, out var el)
        && el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty("SDR", out var sdr)
        && sdr.ValueKind == JsonValueKind.Number
            ? sdr.GetDouble()
            : null;
}

// ── JSON DTOs ─────────────────────────────────────────────────────────────────

/// <summary>One model entry from list_models.py: { filename, stems, scores: { stem: { SDR } } }.
/// The script's "target_stem" field is unused and intentionally not modelled.</summary>
internal sealed record ModelEntryDto
{
    public string? Filename { get; init; }
    public List<string> Stems { get; init; } = [];

    /// <summary>
    /// Raw per-stem score map. Held as <see cref="JsonElement"/> values because upstream mixes
    /// per-stem score objects with scalar metrics (e.g. "seconds_per_minute_m3") under the same
    /// map; <see cref="ModelCatalogService.SdrFor"/> reads SDR only from the object values.
    /// </summary>
    public Dictionary<string, JsonElement>? Scores { get; init; }
}

/// <summary>
/// Source-generated serializer context for the list_models.py catalog. The camelCase policy maps
/// the DTO properties onto the script's lowercase JSON keys; "SDR" is pinned explicitly.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(
    typeof(Dictionary<string, Dictionary<string, ModelEntryDto>>),
    TypeInfoPropertyName = "ModelCatalog"
)]
internal sealed partial class ModelCatalogJsonContext : JsonSerializerContext { }

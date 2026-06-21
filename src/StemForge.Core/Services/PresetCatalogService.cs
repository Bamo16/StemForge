using System.Text.Json;
using System.Text.Json.Serialization;
using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// Resolves the built-in ensemble preset catalog by running the torch-free <c>list_presets.py</c>
/// one-shot (via <see cref="LightweightCatalog"/>) and mapping the result into <see cref="Preset"/>
/// models. Used at GUI startup and by the CLI <c>presets</c> command; this avoids spinning up the
/// long-lived (torch-loading) separator driver just to list presets. Results are cached after first
/// load. Returns an empty list on toolchain absence or parse failure.
/// </summary>
public sealed class PresetCatalogService(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;
    private IReadOnlyList<Preset>? _cache;

    public void Invalidate() => _cache = null;

    public async Task<IReadOnlyList<Preset>> ListPresetsAsync(CancellationToken ct = default)
    {
        if (_cache is not null)
            return _cache;

        var raw = await LightweightCatalog.RunScriptAsync(
            _runner,
            _paths,
            AppPaths.ListPresetsScript,
            [],
            "PresetCatalog",
            ct
        );

        _cache = ParsePresets(raw);
        return _cache;
    }

    // ── Parsing / mapping ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses the <c>list_presets.py</c> JSON (preset id -> {name, description, models, algorithm})
    /// into <see cref="Preset"/> models, inferring category and trimming the redundant category
    /// prefix from each label. The static built-in fallback equivalent lives in
    /// <see cref="PresetCatalog"/>.
    /// </summary>
    internal static IReadOnlyList<Preset> ParsePresets(string? raw)
    {
        var json = LightweightCatalog.ExtractJsonObject(raw);
        if (json is null)
            return [];

        Dictionary<string, PresetEntryDto>? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize(
                json,
                PresetCatalogJsonContext.Default.PresetCatalog
            );
        }
        catch
        {
            return [];
        }

        if (catalog is null)
            return [];

        var list = new List<Preset>(catalog.Count);
        foreach (var (id, entry) in catalog)
        {
            var category = InferCategory(id);
            var name = entry.Name ?? "";
            var label = StripCategoryPrefix(name.Length > 0 ? name : id, category);

            list.Add(
                new Preset(
                    id,
                    label,
                    category,
                    entry.Description ?? "",
                    ModelCount: entry.Models.Count,
                    Vram: string.Empty,
                    Models: entry.Models,
                    EnsembleAlgorithm: string.IsNullOrWhiteSpace(entry.Algorithm)
                        ? null
                        : entry.Algorithm
                )
            );
        }

        return list;
    }

    public static PresetCategory InferCategory(string id) =>
        id.StartsWith("vocal_") ? PresetCategory.Vocals
        : id.StartsWith("instrumental_") || id == "karaoke" ? PresetCategory.Instrumentals
        : PresetCategory.Other;

    public static string StripCategoryPrefix(string name, PresetCategory category)
    {
        var prefix = category switch
        {
            PresetCategory.Vocals => "Vocal ",
            PresetCategory.Instrumentals => "Instrumental ",
            _ => "",
        };
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name;
    }
}

// ── JSON DTOs ─────────────────────────────────────────────────────────────────

/// <summary>One preset from list_presets.py. Mirrors audio-separator's ensemble_presets.json entry
/// shape; "weights" is unused by StemForge and intentionally not modelled.</summary>
internal sealed record PresetEntryDto
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public List<string> Models { get; init; } = [];
    public string? Algorithm { get; init; }
}

/// <summary>
/// Source-generated serializer context for the list_presets.py catalog. The camelCase policy maps
/// the DTO properties onto the script's lowercase JSON keys.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(
    typeof(Dictionary<string, PresetEntryDto>),
    TypeInfoPropertyName = "PresetCatalog"
)]
internal sealed partial class PresetCatalogJsonContext : JsonSerializerContext { }

using System.Text.Json;
using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// Resolves the built-in ensemble preset catalog by running the torch-free <c>list_presets.py</c>
/// one-shot against the audio-separator Python interpreter and mapping the result into
/// <see cref="Preset"/> models. Used at GUI startup and by the CLI <c>presets</c> command; this
/// avoids spinning up the long-lived (torch-loading) separator driver just to list presets.
/// Results are cached after first load. Returns an empty list on toolchain absence or parse failure.
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

        var script = AppPaths.ListPresetsScript;

        if (!File.Exists(script))
        {
            AppLogger.Warning("PresetCatalog", $"list_presets.py not found at: {script}");
            _cache = [];
            return _cache;
        }

        ProcessRunner.Result result;
        try
        {
            result = await _runner.RunAsync(
                _paths.SeparationDriverPython,
                [script],
                ct,
                logRawLines: false
            );
        }
        catch (Exception ex)
        {
            AppLogger.Warning("PresetCatalog", $"Failed to run list_presets.py: {ex.Message}");
            _cache = [];
            return _cache;
        }

        var raw = string.IsNullOrWhiteSpace(result.Stdout) ? result.Output : result.Stdout;
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
    internal static IReadOnlyList<Preset> ParsePresets(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return [];

        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            var list = new List<Preset>();

            // Top-level keys are preset ids; values are objects with "models", "algorithm", etc.
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var id = prop.Name;
                var models = ReadStringArray(prop.Value, "models");
                var name = ReadString(prop.Value, "name");
                var description = ReadString(prop.Value, "description");
                var algorithm = ReadString(prop.Value, "algorithm");

                var category = InferCategory(id);
                var label = StripCategoryPrefix(name.Length > 0 ? name : id, category);

                list.Add(
                    new Preset(
                        id,
                        label,
                        category,
                        description,
                        ModelCount: models.Count,
                        Vram: string.Empty,
                        Models: models,
                        EnsembleAlgorithm: string.IsNullOrWhiteSpace(algorithm) ? null : algorithm
                    )
                );
            }

            return list;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("PresetCatalog", $"Failed to parse preset JSON: {ex.Message}");
            return [];
        }
    }

    private static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static List<string> ReadStringArray(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in el.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String)
                    list.Add(m.GetString()!);
            }
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

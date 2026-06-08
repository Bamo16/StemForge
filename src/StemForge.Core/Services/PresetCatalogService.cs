using System.Text.Json;
using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// Runs list_presets.py via the audio-separator Python interpreter and parses the result.
/// Results are cached after first load. Returns an empty list on toolchain absence or parse failure.
/// </summary>
public sealed class PresetCatalogService(IProcessRunner runner, AppPaths paths)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;
    private IReadOnlyList<PresetInfo>? _cache;

    public void Invalidate() => _cache = null;

    public async Task<IReadOnlyList<PresetInfo>> ListPresetsAsync(CancellationToken ct = default)
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
        var presets = TryParseJson(raw);
        _cache = presets;
        return _cache;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    internal static IReadOnlyList<PresetInfo> TryParseJson(string raw)
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

            var list = new List<PresetInfo>();

            // Top-level keys are preset IDs, values are objects with "models", "algorithm", etc.
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var id = prop.Name;
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var models = new List<string>();
                if (
                    prop.Value.TryGetProperty("models", out var modelsEl)
                    && modelsEl.ValueKind == JsonValueKind.Array
                )
                {
                    foreach (var m in modelsEl.EnumerateArray())
                    {
                        if (m.ValueKind == JsonValueKind.String)
                            models.Add(m.GetString()!);
                    }
                }

                var algorithm = "";
                if (
                    prop.Value.TryGetProperty("algorithm", out var algEl)
                    && algEl.ValueKind == JsonValueKind.String
                )
                {
                    algorithm = algEl.GetString() ?? "";
                }

                list.Add(new PresetInfo(id, models, algorithm));
            }

            return list;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("PresetCatalog", $"Failed to parse preset JSON: {ex.Message}");
            return [];
        }
    }
}

using System.Text.Json;
using StemForge.Models;

namespace StemForge.Services;

/// <summary>
/// Runs audio-separator --list_models --list_format=json and parses the result.
/// Results are cached until Invalidate() or a forced refresh.
/// </summary>
public sealed class ModelCatalogService
{
    private IReadOnlyList<ModelInfo>? _cache;

    public void Invalidate() => _cache = null;

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        string audioSeparatorExe,
        bool forceRefresh = false,
        CancellationToken ct = default
    )
    {
        if (!forceRefresh && _cache is not null)
            return _cache;

        var result = await ProcessRunner.RunAsync(
            audioSeparatorExe,
            ["--list_models", "--list_format=json"],
            ct
        );

        // JSON goes to stdout; fall back to combined output if stdout is empty.
        var raw = string.IsNullOrWhiteSpace(result.Stdout) ? result.Output : result.Stdout;

        var models = TryParseJson(raw);
        _cache = models;
        return models;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<ModelInfo> TryParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        // The output is a nested object: { "Arch": { "Friendly Name": { ...model... } } }
        // Strip any log lines before/after the JSON object.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return [];

        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            var list = new List<ModelInfo>();

            // Top-level keys are architecture names: "MDX", "Demucs", "MDXC", "VR Arch"
            foreach (var archProp in doc.RootElement.EnumerateObject())
            {
                var arch = archProp.Name;
                if (archProp.Value.ValueKind != JsonValueKind.Object)
                    continue;

                // Second level keys are the human-readable model names.
                foreach (var modelProp in archProp.Value.EnumerateObject())
                {
                    var friendlyName = modelProp.Name;
                    var modelEl = modelProp.Value;
                    if (modelEl.ValueKind != JsonValueKind.Object)
                        continue;

                    var filename = GetString(modelEl, "filename");
                    if (string.IsNullOrWhiteSpace(filename))
                        continue;

                    var stems = ParseStems(modelEl);
                    list.Add(new ModelInfo(filename, arch, friendlyName, stems));
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    // Pull stems from the "stems" string array and look up SDR from "scores".
    private static IReadOnlyList<StemSdr> ParseStems(JsonElement modelEl)
    {
        var result = new List<StemSdr>();

        // "stems": ["vocals", "instrumental", ...]
        if (
            !modelEl.TryGetProperty("stems", out var stemsArr)
            || stemsArr.ValueKind != JsonValueKind.Array
        )
            return result;

        // "scores": { "vocals": { "SDR": 10.15, ... }, ... }
        modelEl.TryGetProperty("scores", out var scoresEl);

        foreach (var stemEl in stemsArr.EnumerateArray())
        {
            if (stemEl.ValueKind != JsonValueKind.String)
                continue;

            var name = stemEl.GetString()!;
            double? sdr = null;

            if (
                scoresEl.ValueKind == JsonValueKind.Object
                && scoresEl.TryGetProperty(name, out var stemScoreEl)
                && stemScoreEl.ValueKind == JsonValueKind.Object
                && stemScoreEl.TryGetProperty("SDR", out var sdrEl)
                && sdrEl.ValueKind == JsonValueKind.Number
            )
            {
                sdr = sdrEl.GetDouble();
            }

            result.Add(new StemSdr(name, sdr));
        }

        return result;
    }

    private static string? GetString(JsonElement el, params string[] keys)
    {
        foreach (var key in keys)
            if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        return null;
    }
}

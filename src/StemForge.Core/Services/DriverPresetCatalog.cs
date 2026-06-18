using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// Maps the driver's live <c>presets</c> event payload into the app's <see cref="Preset"/> model,
/// inferring category and trimming the redundant category prefix from each label. This is a
/// preset-catalog concern, kept out of the driver service, which only manages the process and
/// protocol. The static built-in fallback equivalent lives in <see cref="PresetCatalog"/>.
/// </summary>
internal static class DriverPresetCatalog
{
    public static IReadOnlyList<Preset> ToPresets(
        IReadOnlyDictionary<string, DriverPresetEntry> entries
    )
    {
        var result = new List<Preset>(entries.Count);
        foreach (var (id, entry) in entries)
        {
            var category = InferCategory(id);
            var label = StripCategoryPrefix(entry.Name.Length > 0 ? entry.Name : id, category);
            result.Add(
                new Preset(
                    id,
                    label,
                    category,
                    entry.Description,
                    ModelCount: entry.Models.Count,
                    Vram: string.Empty,
                    Models: entry.Models,
                    EnsembleAlgorithm: string.IsNullOrWhiteSpace(entry.Algorithm)
                        ? null
                        : entry.Algorithm
                )
            );
        }
        return result;
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

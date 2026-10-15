using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Core.Catalog;

/// <summary>Hand-written notes on the models StemForge relies on, keyed by model filename (#93).</summary>
public static class ModelNotes
{
    private const string Resource = "StemForge.Core.Catalog.model-notes.json";

    private static readonly Lazy<IReadOnlyDictionary<string, ModelNote>> All = new(Load);

    public static ModelNote? For(string filename) => All.Value.GetValueOrDefault(filename);

    internal static IReadOnlyDictionary<string, ModelNote> Load()
    {
        using var stream =
            typeof(ModelNotes).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Missing embedded resource {Resource}.");
        var notes =
            JsonSerializer.Deserialize(stream, ModelNotesJsonContext.Default.ModelNotes)
            ?? throw new InvalidOperationException($"{Resource} is empty.");

        return new Dictionary<string, ModelNote>(notes, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>What a model is for, in StemForge's words, and where its claims can be checked.</summary>
public sealed record ModelNote(string Summary, IReadOnlyList<ModelNoteLink> Links, string? Author);

public sealed record ModelNoteLink(string Label, string Url);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, ModelNote>), TypeInfoPropertyName = "ModelNotes")]
internal sealed partial class ModelNotesJsonContext : JsonSerializerContext { }

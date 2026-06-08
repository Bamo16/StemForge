namespace StemForge.Core.Models;

public sealed record PresetInfo(string Id, IReadOnlyList<string> Models, string Algorithm);

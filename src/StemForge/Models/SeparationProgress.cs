namespace StemForge.Models;

public sealed record SeparationProgress(int OverallPercent, string StatusText, int? ChunkPercent);

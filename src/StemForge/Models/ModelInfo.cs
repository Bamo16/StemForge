namespace StemForge.Models;

public sealed record StemSdr(string Name, double? Sdr);

public sealed record ModelInfo(
    string Filename,
    string Architecture,
    string FriendlyName,
    IReadOnlyList<StemSdr> Stems
);

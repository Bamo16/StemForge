namespace StemForge.Core.Catalog;

public sealed record StemSdr(string Name, double? Sdr);

/// <summary>A catalog model; <see cref="Files"/> are the local file names it needs, weights and configs.</summary>
public sealed record ModelInfo(
    string Filename,
    string Architecture,
    string FriendlyName,
    IReadOnlyList<StemSdr> Stems,
    IReadOnlyList<string> Files
);

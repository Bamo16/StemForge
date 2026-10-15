namespace StemForge.Core.Catalog;

public sealed record StemSdr(string Name, double? Sdr);

/// <summary>A catalog model; <see cref="Files"/> are the local file names it needs, weights and configs.</summary>
public sealed record ModelInfo(
    string Filename,
    string Architecture,
    string FriendlyName,
    IReadOnlyList<StemSdr> Stems,
    IReadOnlyList<string> Files
)
{
    /// <summary>Whether every file the model needs is in <paramref name="modelsDirectory"/>.</summary>
    public bool IsDownloadedIn(string modelsDirectory) =>
        Files.All(file => File.Exists(Path.Combine(modelsDirectory, file)));

    /// <summary>Bytes taken by those of the model's files present in <paramref name="modelsDirectory"/>.</summary>
    public long SizeIn(string modelsDirectory) =>
        Files.Sum(file =>
            new FileInfo(Path.Combine(modelsDirectory, file)) is { Exists: true } info
                ? info.Length
                : 0
        );
}

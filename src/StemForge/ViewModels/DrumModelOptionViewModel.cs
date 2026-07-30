namespace StemForge.ViewModels;

/// <summary>
/// One entry in the drum-extraction model picker (issue #70). Wraps a <see cref="DrumModelOption"/>
/// and exposes display strings for the ComboBox: the friendly name plus a downloaded-vs-fetch hint.
/// </summary>
public sealed class DrumModelOptionViewModel(DrumModelOption option)
{
    public DrumModelOption Option { get; } = option;

    public string Filename => Option.Filename;
    public string FriendlyName => Option.FriendlyName;
    public string Architecture => Option.Architecture;
    public bool IsLocal => Option.IsLocal;

    /// <summary>"Downloaded" for a model on disk, "Fetched on use" for one downloaded on first run.</summary>
    public string StateLabel => IsLocal ? "Downloaded" : "Fetched on use";

    public override string ToString() => FriendlyName;
}

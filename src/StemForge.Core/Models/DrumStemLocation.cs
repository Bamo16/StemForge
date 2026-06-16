namespace StemForge.Core.Models;

public enum DrumStemLocation
{
    /// <summary>Drum stem is written alongside the other separation outputs.</summary>
    WithStems,

    /// <summary>Drum stem is written to a cache directory and not surfaced to the user.
    /// Useful when drums are needed internally (e.g. BPM detection) but the user
    /// doesn't want an extra file in their output folder.</summary>
    CacheOnly,
}

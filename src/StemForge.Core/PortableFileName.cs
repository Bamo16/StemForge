using System.Buffers;

namespace StemForge.Core;

/// <summary>
/// Characters kept out of every file name StemForge writes: Windows' reserved set and control
/// characters, on every OS, so a name is the same wherever it is made and survives being copied.
/// </summary>
public static class PortableFileName
{
    public static readonly SearchValues<char> InvalidChars = SearchValues.Create([
        .. "<>:\"/\\|?*",
        .. Enumerable.Range(0, 32).Select(code => (char)code),
    ]);
}

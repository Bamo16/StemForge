using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StemForge.Core;

/// <summary>
/// Shortens the "Artist - Title" part of a file name so every file StemForge names from it, longest
/// suffix included, fits the 255-character limit on a file name and, without long paths, MAX_PATH.
/// </summary>
public static class FileTitle
{
    /// <summary>The longest file name Windows (NTFS) accepts, in UTF-16 code units.</summary>
    public const int MaxComponentLength = 255;

    /// <summary>
    /// Room kept after the title for what follows it. The longest suffix today is
    /// " (Instrumental - Balanced) (99).flac.asd" (40, the .asd being Ableton's analysis file
    /// beside a sample); the rest is headroom for live catalog labels.
    /// </summary>
    public const int SuffixReserve = 60;

    /// <summary>The longest title a name is built from.</summary>
    public const int MaxLength = MaxComponentLength - SuffixReserve;

    // A shortened title keeps at least this much, however deep the folder it is written to.
    private const int MinLength = 48;

    // The longest full path Win32 accepts without long paths: MAX_PATH less its terminating NUL.
    private const int MaxPath = 259;

    // An artist list is cut before the song title, but keeps at least this much to stay readable.
    private const int ArtistFloor = 40;

    private const string Separator = " - ";

    /// <summary>
    /// The longest title for files written to <paramref name="directory"/>: <see cref="MaxLength"/>,
    /// or less without long paths where the folder's own path would push a name past MAX_PATH.
    /// </summary>
    public static int MaxLengthIn(string directory, bool longPathsEnabled) =>
        longPathsEnabled
            ? MaxLength
            : Math.Clamp(
                MaxPath
                    - Path.GetFullPath(directory is { Length: > 0 } ? directory : ".").Length
                    - 1
                    - SuffixReserve,
                MinLength,
                MaxLength
            );

    /// <summary>
    /// <paramref name="title"/> unchanged when it fits, otherwise cut at a grapheme boundary (the
    /// artist list before the song title), marked with an ellipsis, and ended with a hash of the
    /// whole title so two cut titles never collide. The same title always gives the same name.
    /// </summary>
    public static string Fit(string title, int maxLength = MaxLength)
    {
        maxLength = Math.Max(maxLength, MinLength);
        if (title.Length <= maxLength)
            return title;

        var tag = $" [{Hash(title)}]";
        var room = maxLength - tag.Length;

        if (
            title.Split(Separator, 2) is [{ Length: > 0 } artist, { Length: > 0 } song]
            && Math.Min(artist.Length, ArtistFloor) + Separator.Length < room
        )
        {
            var forArtist = room - Separator.Length - song.Length;
            var cutArtist = Shorten(
                artist,
                Math.Max(forArtist, Math.Min(artist.Length, ArtistFloor))
            );
            var cutSong = Shorten(song, room - Separator.Length - cutArtist.Length);

            return $"{cutArtist}{Separator}{cutSong}{tag}";
        }

        return $"{Shorten(title, room)}{tag}";
    }

    // At most maxLength, ending in an ellipsis when anything was cut.
    private static string Shorten(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{Head(text, maxLength - 1)}…";

    // The longest run of whole graphemes within maxLength, so no surrogate pair or combining mark
    // is split, without the spaces or punctuation the cut leaves dangling.
    private static string Head(string text, int maxLength)
    {
        var end = 0;
        while (end < text.Length)
        {
            var next = end + StringInfo.GetNextTextElementLength(text, end);
            if (next > maxLength)
                break;

            end = next;
        }

        return text[..end].TrimEnd(' ', ',', '-', '(', '[', '&', '/');
    }

    private static string Hash(string title) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title)))[..6];
}

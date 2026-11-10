using System.Text.RegularExpressions;

namespace StemForge.Core.Downloading;

/// <summary>
/// Removes a video's packaging from its title: "(Official Video)", "[Lyric Video]", "【MV】" and
/// the like. A remix, version, feature or live marker is part of the song and is never touched.
/// </summary>
internal static partial class TitlePackaging
{
    /// <summary>
    /// <paramref name="title"/> without any bracketed packaging phrase, and without the space before
    /// it. Unchanged when nothing matches, and when removing it would leave nothing.
    /// </summary>
    public static string Strip(string title)
    {
        var stripped = title;
        string previous;

        // An outer bracket can only be read once the packaging inside it is gone: "(Official Video
        // [HD])" loses "[HD]" first, then the rest.
        do
        {
            previous = stripped;
            stripped = Bracketed.Replace(
                stripped,
                match => Phrase.IsMatch(match.Groups["Content"].Value) ? string.Empty : match.Value
            );
        } while (stripped != previous);

        return stripped.Trim() is { Length: > 0 } result ? result : title;
    }

    /// <summary>
    /// A bracket pair with no bracket inside it, in the four kinds titles use: round, square,
    /// Japanese corner brackets and full-width round. The space before it goes with it.
    /// </summary>
    [GeneratedRegex(
        @"
        \s*
        (
            \(  \s* (?<Content>[^()\[\]【】（）]*?) \s* \)
            |
            \[  \s* (?<Content>[^()\[\]【】（）]*?) \s* \]
            |
            【  \s* (?<Content>[^()\[\]【】（）]*?) \s* 】
            |
            （  \s* (?<Content>[^()\[\]【】（）]*?) \s* ）
        )
        ",
        RegexOptions.ExplicitCapture | RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex Bracketed { get; }

    /// <summary>The whole content of a bracket when it is nothing but packaging.</summary>
    [GeneratedRegex(
        @"
        ^
        (
            official \s+ (hd\s+|4k\s+|uhd\s+)? (music\s+|lyrics?\s+|audio\s+)?
                (video|audio|visuali[sz]er|mv|lyrics?) (\s+(hd|4k|uhd))?
            |
            (music|lyrics?|audio) \s+ (video|visuali[sz]er)
            |
            (official\s+)? (mv|lyrics?|visuali[sz]er)
            |
            (hd|4k|uhd|hq) (\s+(remaster(ed)?|video|audio|quality|upgrade|version|restored))?
            |
            remaster(ed)? \s+ (in\s+|to\s+)? (hd|4k|uhd)
        )
        $
        ",
        RegexOptions.ExplicitCapture
            | RegexOptions.IgnorePatternWhitespace
            | RegexOptions.IgnoreCase
    )]
    private static partial Regex Phrase { get; }
}

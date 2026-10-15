using System.Text.RegularExpressions;

namespace StemForge.Core.Downloading;

/// <summary>
/// Shapes the artist metadata yt-dlp reports into the two forms StemForge needs.
///
/// The two operations are deliberately separate because they answer different questions.
/// <see cref="Canonical"/> removes a yt-dlp artifact: a YouTube Music track routinely repeats the
/// same contributor once per role, so one source returns eleven entries naming three artists. Those
/// repeats are not information, so the deduplicated list is the truth and is used everywhere,
/// including the file's own ARTIST tag. <see cref="WithoutFeatured"/> discards a real fact to
/// shorten a name: a featured artist named in the title is still genuinely an artist on the track,
/// so it is dropped from the <em>filename</em> only, never from metadata.
/// </summary>
internal static partial class ArtistNames
{
    /// <summary>
    /// The artist list with repeats collapsed, preserving first-seen order and casing. Prefers the
    /// structured <c>artists</c> array; splits the flattened <c>artist</c> string only when a
    /// source omits the array, since that string is yt-dlp's own format and there is nothing else
    /// to read. Empty when no artist is known, which is what marks a source as an ordinary upload
    /// rather than a music track entity.
    /// </summary>
    public static IReadOnlyList<string> Canonical(IReadOnlyList<string>? artists, string? flattened)
    {
        var source = artists is { Count: > 0 } ? artists : Split(flattened);
        List<string> unique = [];
        HashSet<string> seen = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var name in source)
            if (name.Trim() is { Length: > 0 } trimmed && seen.Add(trimmed))
                unique.Add(trimmed);

        return unique;
    }

    /// <summary>
    /// The artist list with any artist the title already credits as a featured performer removed,
    /// so a name does not say the same thing twice. Matching is confined to an explicit
    /// <c>feat.</c> / <c>ft.</c> / <c>featuring</c> segment and requires a whole-name match: a bare
    /// substring scan would drop an artist called "Bird" from "BIRDBRAIN".
    ///
    /// Returns the input unchanged when the rule would remove every artist, so a single-artist
    /// track titled "Song (feat. That Artist)" keeps its artist rather than losing it entirely.
    /// </summary>
    public static IReadOnlyList<string> WithoutFeatured(IReadOnlyList<string> artists, string title)
    {
        if (artists is not { Count: > 0 } || FeaturedNames(title) is not { Count: > 0 } featured)
            return artists;

        List<string> kept = [.. artists.Where(name => !featured.Contains(name))];

        // Never leave a track artist-less: if the title credits everyone, keep everyone.
        return kept.Count > 0 ? kept : artists;
    }

    /// <summary>
    /// Every name credited in a featured-artist segment of <paramref name="title"/>. The segment
    /// need not be bracketed: "Song feat. A, B" runs to the end of the title, while "Song (feat. A)
    /// (Remix)" stops at the closing bracket.
    ///
    /// A segment yields both itself and its separated parts as candidates, because a title gives no
    /// way to tell "one artist whose name has a comma" from "two artists" — offering both readings
    /// lets the artist list, which does know, decide. Nothing matches, and nothing is dropped,
    /// unless a real credited artist is named.
    /// </summary>
    private static HashSet<string> FeaturedNames(string title)
    {
        HashSet<string> names = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (Match match in FeaturedSegment.Matches(title))
        {
            var segment = match.Groups["names"].Value.Trim();

            // The whole segment counts as one candidate before it is broken up, so a credited name
            // that itself contains a separator ("feat. Tyler, The Creator") still matches the
            // artist entry. Splitting alone would look for "Tyler" and "The Creator" and find
            // neither. Adding it cannot produce a false drop: matching the whole segment requires
            // an artist to be named exactly that.
            if (segment.Length > 0)
                names.Add(segment);

            foreach (var name in Split(segment))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Splits free text on the separators a credit list uses, trimming and dropping empties. Only
    /// ever applied to text that arrives as text: yt-dlp's flattened artist string, and a featured
    /// segment parsed out of a title. Never to a list this class produced.
    /// </summary>
    private static List<string> Split(string? value) =>
        value is not { Length: > 0 }
            ? []
            : [.. NameSeparator.Split(value).Select(n => n.Trim()).Where(n => n.Length > 0)];

    /// <summary>
    /// Opens on an optional bracket and a featured marker, then captures to the closing bracket or
    /// the end of the title. <c>\b</c> around the marker keeps "Feature" and "Fifty" from matching.
    /// </summary>
    [GeneratedRegex(
        @"[(\[]?\s*\b(?:feat|ft|featuring)\b\.?\s*(?<names>[^)\]]*)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex FeaturedSegment { get; }

    /// <summary>Separators yt-dlp and uploaders use between credited names.</summary>
    [GeneratedRegex(@"[,;&]|\s+\band\b\s+", RegexOptions.IgnoreCase)]
    private static partial Regex NameSeparator { get; }
}

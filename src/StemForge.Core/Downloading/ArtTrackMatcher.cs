using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace StemForge.Core.Downloading;

/// <summary>A video or song as YouTube Music lists it; <see cref="IsArtTrack"/> is musicVideoType ATV.</summary>
internal sealed record MusicEntry(
    string VideoId,
    string Title,
    string Artist,
    int LengthSeconds,
    bool IsArtTrack
);

internal static partial class ArtTrackMatcher
{
    public const int ToleranceSeconds = 2;

    /// <summary>
    /// Picks the Art Track among <paramref name="hits"/>: an ATV whose length is within
    /// <see cref="ToleranceSeconds"/> of the video's and whose title says the same thing once
    /// brackets are ignored. The closest length wins.
    /// </summary>
    public static bool TryPick(
        MusicEntry video,
        IEnumerable<MusicEntry> hits,
        [NotNullWhen(true)] out ArtTrackSuggestion? pick
    )
    {
        var wanted = Normalize(video.Title);
        pick = hits.Where(hit =>
                hit is { IsArtTrack: true, VideoId: var id }
                && id != video.VideoId
                && Math.Abs(hit.LengthSeconds - video.LengthSeconds) <= ToleranceSeconds
                && Normalize(hit.Title) == wanted
            )
            .OrderBy(hit => Math.Abs(hit.LengthSeconds - video.LengthSeconds))
            .Select(hit => new ArtTrackSuggestion(
                hit.VideoId,
                hit.Title,
                hit.Artist,
                hit.LengthSeconds,
                hit.LengthSeconds - video.LengthSeconds
            ))
            .FirstOrDefault();

        return pick is not null;
    }

    /// <summary>The title without a trailing "(Official Video)" style bracket, for searching.</summary>
    public static string WithoutOfficialSuffix(string title) =>
        OfficialSuffix.Replace(title, string.Empty).Trim();

    /// <summary>Lower-case letters and digits only, with bracketed parts dropped.</summary>
    internal static string Normalize(string title) =>
        new([
            .. Bracketed
                .Replace(title, " ")
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant),
        ]);

    [GeneratedRegex(
        @"\s*[\(\[][^\)\]]*\b(official|lyrics?|audio|video|visuali[sz]er)\b[^\)\]]*[\)\]]\s*$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex OfficialSuffix { get; }

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Bracketed { get; }
}

using System.Diagnostics.CodeAnalysis;

namespace StemForge.Core.Downloading;

public sealed record YtDlpMetadata(
    string SourceUrl,
    string Title,
    string? Artist,
    string? Uploader,
    string? SourceCodec,
    double? SourceBitrateKbps,
    double? DurationSeconds,
    string? FormatId,
    string MediaUrl,
    string? ThumbnailUrl = null,
    IReadOnlyList<YtDlpFormat>? AudioFormats = null,
    string? Extractor = null
)
{
    /// <summary>"Artist - Title" when artist is available, plain Title otherwise.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";

    /// <summary>
    /// True when this came from YouTube. Format ids mean different things per extractor, so every
    /// gating and premium inference is scoped to it.
    /// </summary>
    public bool IsYouTube =>
        Extractor is { } extractor
        && extractor.Equals("youtube", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when YouTube surfaces this as a music track rather than an ordinary upload. Artist
    /// metadata is the marker: it is populated on YouTube Music track entities (typically served
    /// from a <c>- Topic</c> channel, alongside track and album) and absent from video uploads,
    /// including official ones from the artist's or label's own channel. Artist alone is
    /// sufficient: track, album and the plural artists field select an identical set, while the
    /// <c>- Topic</c> channel name disagrees on 102 of 552 corpus sources and is the wrong signal.
    /// </summary>
    public bool IsMusicTrackEntity => !string.IsNullOrWhiteSpace(Artist);

    /// <summary>
    /// True when the format actually selected for download is premium: gated behind
    /// authentication <em>and</em> better than anything this source offers ungated.
    /// </summary>
    public bool SelectedFormatIsPremium =>
        AudioFormats is { } formats
        && formats.FirstOrDefault(f => f.FormatId == FormatId) is { } selected
        && PremiumFormats.IsPremium(selected, formats, Extractor);

    /// <summary>True when any candidate format is premium by that same measure.</summary>
    public bool OffersPremiumFormat =>
        AudioFormats is { } formats
        && formats.Any(f => PremiumFormats.IsPremium(f, formats, Extractor));

    /// <summary>
    /// True when any candidate format is authentication-gated regardless of its bitrate. Proves
    /// the browser session is still signed in, even when it bought nothing worth having.
    /// </summary>
    public bool HasAuthenticatedSessionEvidence =>
        AudioFormats is { } formats && PremiumFormats.AnyAuthGated(formats, Extractor);

    /// <summary>
    /// Re-points this metadata at a specific candidate format instead of the one the selection
    /// policy chose, for callers that need identical acquisition across a set rather than the
    /// best result per source. Returns false when the source does not offer that format, or
    /// offers it without a directly streamable URL; callers are expected to fail rather than
    /// fall back, since a silent substitution is what pinning exists to prevent.
    /// </summary>
    public bool TryPinFormat(string formatId, [NotNullWhen(true)] out YtDlpMetadata? pinned)
    {
        // Exact id first. Failing that, match on the bare itag: on a video with AI auto-dubs the
        // bare id does not exist at all (140 becomes 140-0 … 140-N, one per language), so pinning
        // "140" would fail on a source that plainly offers it. The candidate list has already had
        // dubs removed, so an itag match resolves to the original-language track, and the
        // non-DRC variant is preferred. This is not a silent substitution of a different format:
        // the suffix is a per-video index onto the same itag, and the id actually used is reported
        // back in the result.
        var match =
            AudioFormats?.FirstOrDefault(f =>
                string.Equals(f.FormatId, formatId, StringComparison.Ordinal)
            )
            ?? AudioFormats
                ?.Where(f =>
                    f.Itag is { } itag
                    && string.Equals(
                        itag,
                        AudioFormatInfo.BareItag(formatId),
                        StringComparison.Ordinal
                    )
                )
                .OrderBy(f => f.IsDrc)
                .FirstOrDefault();

        if (match is not { Url: { } url })
        {
            pinned = null;
            return false;
        }

        pinned = this with
        {
            FormatId = match.FormatId,
            SourceCodec = match.AudioCodec,
            SourceBitrateKbps = match.AudioBitrate,
            MediaUrl = url,
        };
        return true;
    }
}

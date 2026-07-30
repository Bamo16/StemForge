namespace StemForge.Core.Downloading;

/// <summary>
/// Decides which candidate formats count as premium, in the sense the product cares about:
/// <b>a higher bitrate than the source offers without authentication</b>.
///
/// Gating alone is not enough, because gating comes in two tiers. Format 250 (opus, around
/// 65 kbps) is merely <i>session</i>-gated: any signed-in account is shown it, paying or not, and
/// it loses to the freely-available format 140 at 130 kbps. Calling it premium would tell a user
/// they received something better than they did. Only 141 and 774 are withheld from a signed-in
/// free account, and they are also the only gated formats that ever clear the bar below, so the
/// bitrate test and the entitlement tier agree without this needing to know about tiers at all.
/// Measured across a 552-video corpus: 250 was gated on 486 sources and beat the ungated baseline
/// on none of them.
///
/// The comparison is made within a single candidate list rather than against a hardcoded table,
/// since the ungated candidates are offered regardless of authentication and so stand in for what
/// the request would have received without it. Not an exact stand-in: signing in also withdraws
/// format 139, so an anonymous request sees one format a signed-in request does not. It never
/// changes the outcome, as 139 is the lowest rung at around 49 kbps and never sets the bar.
/// </summary>
public static class PremiumFormats
{
    /// <summary>
    /// True when <paramref name="format"/> is authentication-gated and beats every format the
    /// source offers ungated. <paramref name="all"/> must be the full candidate list.
    /// </summary>
    public static bool IsPremium(
        YtDlpFormat format,
        IReadOnlyList<YtDlpFormat> all,
        string? extractor
    ) =>
        AudioFormatInfo.IsAuthGated(format.FormatId, extractor)
        && format.AudioBitrate > BestUngatedBitrate(all, extractor);

    /// <summary>
    /// True when any candidate is authentication-gated, whatever its bitrate and whichever tier.
    /// This is the session canary: YouTube withholds these from an anonymous request, so their
    /// presence proves the cookies still carry a live session even when none of them is an
    /// improvement worth having.
    ///
    /// Deliberately tier-blind, which is what makes it useful. Since the session-gated rungs are
    /// shown to a free account while the Premium ones are not, "gated formats present but none of
    /// them premium" is a signed-in non-subscriber, whereas "nothing gated at all" is a session
    /// that is no longer signed in. Distinguishing those is the whole point of the advisory.
    /// </summary>
    public static bool AnyAuthGated(IReadOnlyList<YtDlpFormat> all, string? extractor) =>
        all.Any(f => AudioFormatInfo.IsAuthGated(f.FormatId, extractor));

    /// <summary>
    /// Highest bitrate among candidates that are not authentication-gated, i.e. the bar a gated
    /// format has to clear to have been worth authenticating for. Zero when every candidate is
    /// gated, which makes any gated format count as an improvement.
    /// </summary>
    public static double BestUngatedBitrate(IReadOnlyList<YtDlpFormat> all, string? extractor) =>
        all.Where(f => !AudioFormatInfo.IsAuthGated(f.FormatId, extractor))
            .Select(f => f.AudioBitrate)
            .DefaultIfEmpty(0)
            .Max();
}

namespace StemForge.Core.Separation.Models;

public static class AudioFormatInfo
{
    // Itags YouTube withholds from an unauthenticated request. There is no upstream source for
    // this: yt-dlp builds its format list from YouTube's player response at runtime and has no
    // concept of a gated format, so the set is empirical and will drift as YouTube changes its
    // formats. Established by resolving a 552-video corpus three times over (logged out, signed
    // in without a subscription, signed in with Premium), 2026-07-29:
    //
    //   itag  offered anonymously   free account   Premium      tier
    //   141   never                 never          413 videos   Premium-gated
    //   774   never                 never          412 videos   Premium-gated
    //   250   2 of 552              480 videos     480 videos   session-gated
    //   139   550 videos            never          never        withdrawn on sign-in
    //   140   always                always         always       ungated
    //   249   always                always         always       ungated
    //   251   always                always         always       ungated
    //
    // Gated does not mean good, and it does not mean paid. 250 is around 65 kbps opus, below the
    // freely-available 140 at 130 kbps, and any signed-in account is shown it. Membership here
    // means only "a request had to be authenticated to see this", which is exactly what makes the
    // set usable as a session canary. Whether a gated format was actually worth having is
    // PremiumFormats' question, and whether the account pays is inferred from which tier survived.
    //
    // The suffixed variants (-drc, and the per-language -N ids) are also session-gated, but they
    // are not represented here: their bare itag is 140/249/251, which is ungated, so IsAuthGated
    // reports false for them. That under-detection is deliberate. It cannot occur on a music
    // track, where the corpus found no DRC and no dubbed variants at all, so the states that
    // depend on this are exact. Elsewhere it only ever errs toward "no session evidence", which
    // prompts the user to check rather than asserting a live session that may not exist. Treating
    // any suffixed id as gated would flip that to the unsafe direction, and would be wrong on the
    // handful of sources that served suffixed ids anonymously.
    private static readonly HashSet<string> _ytAuthGatedItags =
    [
        "141", // AAC LC, 258 kbps, 44.1 kHz — Premium-gated
        "774", // Opus, 257 kbps, 48 kHz — Premium-gated
        "250", // Opus, ~65 kbps, 48 kHz — session-gated, and worse than the free formats
    ];

    /// <summary>Human-friendly codec name for a raw yt-dlp acodec value.</summary>
    public static string PrettyCodec(string? raw) =>
        raw switch
        {
            // MP4A object-type-indication codes — see ISO/IEC 14496-3
            "mp4a.40.2" => "AAC LC",
            "mp4a.40.5" => "HE-AAC",
            "mp4a.40.29" => "HE-AACv2",
            "mp4a.40.34" => "MP3",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "ac-3" or "a52" => "AC-3",
            "ec-3" => "E-AC-3",
            "flac" => "FLAC",
            _ when string.IsNullOrWhiteSpace(raw) => string.Empty,
            _ => raw,
        };

    /// <summary>
    /// True if YouTube withholds this format from an unauthenticated request. An authentication
    /// signal, not a quality one and not an entitlement one: the set spans both gating tiers, and
    /// includes a format worse than what the same source gives away. See
    /// <see cref="PremiumFormats"/> for whether a gated format was worth having.
    /// </summary>
    public static bool IsAuthGated(string? formatId, string? extractor) =>
        formatId is not null
        && extractor is not null
        && extractor.Equals("youtube", StringComparison.OrdinalIgnoreCase)
        && _ytAuthGatedItags.Contains(BareItag(formatId));

    /// <summary>
    /// Strips a trailing per-video suffix from a format id: <c>250-drc</c> and <c>250-9</c> both
    /// reduce to <c>250</c>. Necessary because on a video with AI auto-dubs the bare id ceases to
    /// exist altogether, so a set-membership test against raw ids silently matches nothing and
    /// reports a source as ungated when it is not. The numeric suffix is a per-video language
    /// index, never a stable identifier, so only the itag may be compared against a known set.
    /// </summary>
    public static string BareItag(string formatId) =>
        formatId.IndexOf('-') is var dash && dash > 0 ? formatId[..dash] : formatId;
}

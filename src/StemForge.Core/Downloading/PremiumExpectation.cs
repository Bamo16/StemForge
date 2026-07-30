namespace StemForge.Core.Downloading;

/// <summary>
/// How an acquisition compares against the user's premium expectation. Every outcome other than
/// <see cref="Premium"/> is a [[Premium shortfall]] in glossary terms; they are separated here
/// because each one calls for a different response from the user.
/// </summary>
public enum PremiumStatus
{
    /// <summary>No premium expectation held, or the source is not YouTube. Nothing to report.</summary>
    NotApplicable,

    /// <summary>The format being fetched is premium. The expectation is met.</summary>
    Premium,

    /// <summary>
    /// A music track, signed in, but no Premium format on offer. Music track entities are
    /// reliably provisioned (326/326 in the corpus), and a signed-in <em>free</em> account is
    /// shown the session-gated formats while being withheld the Premium ones, so this is what a
    /// non-Premium account looks like. The most likely cause is cookies read from a browser
    /// profile signed into a different account than intended.
    /// </summary>
    AccountNotPremium,

    /// <summary>
    /// A music track with no authentication evidence at all, not even the session-gated formats a
    /// free account would see. That is the logged-out format set, so the session is not
    /// authenticated.
    /// </summary>
    NotSignedIn,

    /// <summary>
    /// Not a music track, but signed in, and the source offers nothing better than its own free
    /// formats. Gated does not mean good: format 250 is session-gated at around 65 kbps and loses
    /// to the freely-available 140 at 130 kbps. A property of the source, not of the session.
    /// </summary>
    SourceHasNoPremiumAudio,

    /// <summary>
    /// Not a music track and nothing gated at all, so there was no premium ladder to expect. The
    /// ordinary outcome for a video upload (29% of non-music sources in the corpus, with working
    /// cookies). Not a session problem, and the useful thing to say is that a song version may
    /// exist.
    /// </summary>
    NoPremiumLadder,
}

/// <summary>
/// Evaluates whether an acquisition met the user's premium expectation.
///
/// The expectation is inferred from the user having configured a cookie source rather than from
/// a setting of its own: that field is opt-in, blank by default, and exists precisely to obtain
/// YouTube Premium audio, so filling it in is the declaration. A user who never configured
/// cookies holds no expectation and is told nothing about premium formats.
///
/// Two independent facts make the outcome diagnosable, both measured on a 552-video corpus
/// resolved three times over (logged out, signed-in free, signed-in Premium):
///
/// <list type="bullet">
/// <item>Provisioning attaches to the [[Music track entity]], not the video. All 326 music-typed
/// sources carried the full ladder; 29% of non-music sources carried none at all despite working
/// cookies. So "nothing gated" only means something is wrong when the source is a music track.</item>
/// <item>Gating comes in two tiers. 141 and 774 are withheld from a signed-in free account
/// (0/552), while 250 and the suffixed variants are shown to it. So the presence of any gated
/// format proves a session, and the absence of specifically the Premium ones distinguishes a
/// free account from a dead session.</item>
/// </list>
///
/// See ADR 0013.
/// </summary>
public static class PremiumExpectation
{
    /// <summary>True when the user has configured a cookie source, in settings or per-invocation.</summary>
    public static bool IsHeldBy(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.YtdlpCookiesFromBrowser);

    /// <summary>
    /// Compares a resolved source against the expectation. Reads as a decision table: the two
    /// music-track arms are the ones a user can act on, and the two below them are properties of
    /// the source that only merit a note.
    /// </summary>
    public static PremiumStatus Evaluate(YtDlpMetadata meta, bool expectationHeld) =>
        meta switch
        {
            // Nothing is surfaced to a user who never asked for premium audio.
            _ when !expectationHeld => PremiumStatus.NotApplicable,

            // Premium is a YouTube concept; nothing can be inferred about any other source.
            { IsYouTube: false } => PremiumStatus.NotApplicable,

            { SelectedFormatIsPremium: true } => PremiumStatus.Premium,

            // A music track was always provisioned a ladder, so its absence is about the request
            // rather than the source. Which gated formats survived says which: a signed-in free
            // account still sees the session-gated rungs, a logged-out one sees none.
            { IsMusicTrackEntity: true, HasAuthenticatedSessionEvidence: true } =>
                PremiumStatus.AccountNotPremium,

            { IsMusicTrackEntity: true } => PremiumStatus.NotSignedIn,

            // Not a music track, so nothing was promised. Report what the source is like and stay
            // well away from implicating the user's cookies.
            { HasAuthenticatedSessionEvidence: true } => PremiumStatus.SourceHasNoPremiumAudio,

            _ => PremiumStatus.NoPremiumLadder,
        };

    /// <summary>
    /// The advisory text for an outcome, or null when there is nothing to say. Single source for
    /// both front-ends so the wording cannot drift between the GUI tooltip and the CLI's stderr.
    ///
    /// The met outcome deliberately has none. Explaining a success is noise, and the wordmark
    /// alongside the bitrate already shows what was obtained; leaving it silent also keeps the
    /// hover-help affordance meaningful, since it then appears only when something needs saying.
    /// </summary>
    public static string? AdvisoryFor(PremiumStatus status) =>
        status switch
        {
            PremiumStatus.NotSignedIn => NotSignedInMessage,
            PremiumStatus.AccountNotPremium => AccountNotPremiumMessage,
            PremiumStatus.SourceHasNoPremiumAudio => NoPremiumAudioMessage,
            PremiumStatus.NoPremiumLadder => NoPremiumLadderMessage,
            _ => null,
        };

    /// <summary>
    /// Advisory when a music track offered no gated format at all. This is the only case that
    /// points at the session, and it is the one the whole signal exists to catch.
    /// </summary>
    public const string NotSignedInMessage =
        "No Premium format was offered for this track, and neither were the formats a signed-in "
        + "account normally sees. Your browser session is probably no longer signed in to YouTube.";

    /// <summary>
    /// Advisory when a music track yielded session-gated formats but no Premium ones. Names the
    /// browser profile explicitly, because cookies read from a bare browser name follow whichever
    /// profile was used most recently, which is the likely way to end up on the wrong account.
    /// </summary>
    public const string AccountNotPremiumMessage =
        "This track has Premium audio, but it was not offered. The browser session is signed in, "
        + "so the account it belongs to does not have YouTube Premium. If you have more than one "
        + "browser profile, check which one the cookies are being read from.";

    /// <summary>
    /// Advisory when nothing was gated and the source is not a music track entity. Says nothing
    /// about cookies, and instead points at the thing the user can act on.
    /// </summary>
    public const string NoPremiumLadderMessage =
        "This is a video upload rather than a YouTube Music track, and no Premium format was "
        + "offered. If a song version of this track exists, it may offer a higher bitrate.";

    /// <summary>
    /// Advisory when the session is provably fine but a non-music source has nothing better than
    /// its freely-available formats. Deliberately says nothing about cookies.
    /// </summary>
    public const string NoPremiumAudioMessage =
        "This source offers no audio better than its freely-available formats. Your YouTube "
        + "session is signed in, so this is a property of the source, not your cookies.";
}

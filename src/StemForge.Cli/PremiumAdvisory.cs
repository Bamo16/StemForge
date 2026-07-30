namespace StemForge.Cli;

/// <summary>
/// Renders a <see cref="PremiumStatus"/> as a single stderr line, or null when there is nothing
/// worth saying. Shared by every code path that reports on an acquisition so the wording and the
/// warning-versus-note distinction cannot drift between them.
/// </summary>
internal static class PremiumAdvisory
{
    /// <summary>
    /// Null when the outcome needs no comment. "Warning" is reserved for the two outcomes the user
    /// can act on by fixing their setup; everything else is a "Note" about the source, because a
    /// warning the user cannot act on is noise.
    /// </summary>
    internal static string? For(PremiumStatus status) =>
        (status, PremiumExpectation.AdvisoryFor(status)) switch
        {
            (_, null) => null,
            (PremiumStatus.NotSignedIn or PremiumStatus.AccountNotPremium, { } message) =>
                $"Warning: {message}",
            (_, { } message) => $"Note: {message}",
        };
}

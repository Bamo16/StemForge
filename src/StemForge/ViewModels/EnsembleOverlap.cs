using StemForge.Core.Catalog;

namespace StemForge.ViewModels;

/// <summary>
/// One stem name across the selected ensemble and how audio-separator will treat it: AVERAGED when
/// two or more selected models emit that name, PASSED THROUGH when exactly one does. Advisory only —
/// it mirrors how audio-separator groups outputs by stem name and averages the groups of size 2+.
/// </summary>
/// <param name="Name">The stem name (e.g. <c>vocals</c>).</param>
/// <param name="ContributorCount">How many selected models emit this stem name.</param>
public sealed record StemOverlap(string Name, int ContributorCount)
{
    /// <summary>True when 2+ models contribute this stem, so audio-separator averages them.</summary>
    public bool IsAveraged => ContributorCount >= 2;

    /// <summary>True when exactly one model emits this stem, so it passes through unchanged.</summary>
    public bool IsPassthrough => ContributorCount == 1;

    /// <summary>The averaged stem with its contributor count, e.g. <c>vocals (3 models)</c>.</summary>
    public string AveragedDisplay => $"{Name} ({ContributorCount} models)";
}

/// <summary>
/// The advisory result of looking at the selected models' profiles: which stem names will be
/// blended versus passed through, and which selected models have stems StemForge could not resolve
/// (so they are surfaced rather than silently dropped or miscounted as contributors).
/// </summary>
/// <param name="Stems">Every resolved stem name with its contributor count, ordered by name.</param>
/// <param name="UnknownModels">Friendly names of selected models whose stems are unknown.</param>
public sealed record EnsembleOverlapResult(
    IReadOnlyList<StemOverlap> Stems,
    IReadOnlyList<string> UnknownModels
)
{
    public IEnumerable<StemOverlap> Averaged => Stems.Where(s => s.IsAveraged);
    public IEnumerable<StemOverlap> Passthrough => Stems.Where(s => s.IsPassthrough);

    public bool HasAveraged => Stems.Any(s => s.IsAveraged);
    public bool HasPassthrough => Stems.Any(s => s.IsPassthrough);
    public bool HasUnknownModels => UnknownModels.Count > 0;
}

/// <summary>
/// Aggregates the advisory <see cref="ModelProfile"/>s of the selected models into per-stem-name
/// contributor counts, mirroring audio-separator's behaviour: it groups outputs by stem name and
/// averages only the names produced by two or more models; a name from a single model passes through.
/// Purely informational — it never blocks an ensemble. This is the unit-tested core of issue #69.
/// </summary>
public static class EnsembleOverlap
{
    /// <summary>
    /// Counts how many of <paramref name="selections"/> emit each stem name and flags any selection
    /// whose stems are unknown. Stem names are compared case-insensitively and normalised to lower
    /// case. A model contributes a given stem name at most once (a profile listing the same name
    /// twice is not double-counted). Models with unknown stems contribute nothing to the counts;
    /// they are reported in <see cref="EnsembleOverlapResult.UnknownModels"/> instead so the guidance
    /// degrades gracefully rather than misreporting.
    /// </summary>
    /// <param name="selections">
    /// The selected models, each as its friendly name and resolved profile (null/unknown allowed).
    /// </param>
    public static EnsembleOverlapResult Aggregate(
        IEnumerable<(string FriendlyName, ModelProfile? Profile)> selections
    )
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        foreach (var (friendlyName, profile) in selections)
        {
            // No profile, or a profile that resolved no stems, is UNKNOWN: surface it, never count
            // it as a contributor (counting an unknown would misreport averaging/passthrough).
            if (profile is null || profile.IsUnknown)
            {
                unknown.Add(friendlyName);
                continue;
            }

            // A single model emits each stem name at most once for contributor-counting purposes.
            var names = profile
                .Stems.Select(s => s.Name.Trim())
                .Where(n => n.Length > 0)
                .Select(n => n.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
                counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        var stems = counts
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new StemOverlap(kv.Key, kv.Value))
            .ToList();

        return new EnsembleOverlapResult(stems, unknown);
    }
}

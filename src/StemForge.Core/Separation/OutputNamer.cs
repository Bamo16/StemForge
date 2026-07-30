using System.Text;

namespace StemForge.Core.Separation;

/// <summary>
/// Pure, deterministic output-file-name builder shared by every separation run in a job.
///
/// Two responsibilities, both side-effect free:
/// <list type="bullet">
/// <item>
/// Build a stem's output base name (no extension) from the clean "title (stem)" convention, or from
/// an optional per-preset template carrying the <c>title</c>, <c>stem</c>, and <c>preset</c> tokens.
/// This is the same convention the built-in presets already use, now applied to user presets too.
/// </item>
/// <item>
/// Disambiguate names that would collide within a single job. The shared output directory is written
/// by every run in the job, so two runs that resolve to the same base name (e.g. two vocal presets
/// each emitting an "Instrumental" residual) would otherwise overwrite each other. Collisions are
/// resolved by a stable numeric suffix (" (2)", " (3)", …) in the deterministic order the names are
/// reserved — never random, never timestamped.
/// </item>
/// </list>
///
/// A single instance is the naming authority for one job: callers reserve each name through
/// <see cref="Reserve"/>, which records what has already been claimed so the next collision gets the
/// next suffix. Reservation is case-insensitive because the output directory is shared and may live
/// on a case-insensitive filesystem (Windows/macOS).
/// </summary>
public sealed class OutputNamer
{
    // Names already claimed in this job, case-folded for case-insensitive collision detection.
    private readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// The clean default base name for a stem: <c>"{title} ({stem})"</c>. This is the convention the
    /// built-in presets emit, reused verbatim so user presets default to the same shape.
    /// </summary>
    public static string CleanName(string title, string stem) => $"{title} ({stem})";

    /// <summary>
    /// Builds a stem's output base name (no extension, no path) from a template when one is supplied,
    /// otherwise from the clean default. Supported tokens, case-insensitive, in either <c>{title}</c>
    /// or <c>{Title}</c> form: <c>title</c> (the source title), <c>stem</c> (the stem name), and
    /// <c>preset</c> (the preset's display name). Unknown tokens are left literal. The result is
    /// sanitised of path-invalid characters so it is always a usable file name.
    /// </summary>
    public static string BuildName(string? template, string title, string stem, string presetName)
    {
        var raw = string.IsNullOrWhiteSpace(template)
            ? CleanName(title, stem)
            : ExpandTemplate(template, title, stem, presetName);
        return Sanitize(raw);
    }

    private static string ExpandTemplate(
        string template,
        string title,
        string stem,
        string presetName
    )
    {
        var sb = new StringBuilder(template.Length + 32);
        int i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close > i)
                {
                    var token = template[(i + 1)..close].Trim();
                    var replacement = token.ToLowerInvariant() switch
                    {
                        "title" => title,
                        "stem" => stem,
                        "preset" => presetName,
                        _ => (string?)null,
                    };
                    if (replacement is not null)
                    {
                        sb.Append(replacement);
                        i = close + 1;
                        continue;
                    }
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reserves <paramref name="baseName"/> for this job and returns the name to actually use: the
    /// requested name if free, otherwise the same name with the smallest unused " (n)" suffix
    /// (starting at 2). The returned name is recorded as claimed so subsequent reservations of the
    /// same base name receive the next suffix. Deterministic: the suffix is a function only of how
    /// many equal names were reserved before it, never of time or randomness.
    /// </summary>
    public string Reserve(string baseName)
    {
        if (_claimed.Add(baseName))
            return baseName;

        for (int n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (_claimed.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Convenience that builds a name (template or clean default) and reserves it in one step.
    /// </summary>
    public string ResolveAndReserve(
        string? template,
        string title,
        string stem,
        string presetName
    ) => Reserve(BuildName(template, title, stem, presetName));

    internal static string Sanitize(string name) =>
        string.Concat(name.Select(c => _invalidFileNameChars.Contains(c) ? '-' : c)).Trim();
}

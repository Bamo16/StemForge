using System.Text;

namespace StemForge.Core.Separation;

/// <summary>
/// Deterministic output-file-name builder shared by every separation run in a job.
///
/// Two responsibilities:
/// <list type="bullet">
/// <item>
/// Build a stem's output base name (no extension) from the clean "title (stem)" convention, or from
/// an optional per-preset template carrying the <c>title</c>, <c>stem</c>, and <c>preset</c> tokens.
/// This is the same convention the built-in presets already use, now applied to user presets too.
/// </item>
/// <item>
/// Disambiguate names that would collide in the directory being written. Two runs that resolve to
/// the same base name (e.g. two vocal presets each emitting an "Instrumental" residual) would
/// otherwise overwrite each other. Collisions are resolved by a stable numeric suffix
/// (" (2)", " (3)", …) in the deterministic order the names are reserved: never random, never
/// timestamped.
/// </item>
/// </list>
///
/// Claims are tracked <em>per directory</em>, and a directory's claim set is seeded on first use
/// with the base names of files already present there. Both facts matter for correctness:
///
/// <list type="bullet">
/// <item>Seeding from disk is what stops a later job from silently overwriting an earlier job's
/// output. A job-scoped claim set only knows about the runs in its own job, so without this a second
/// job into the same folder resolves to the same clean names and clobbers them.</item>
/// <item>Keying by directory is what stops a drum stem written to its own subfolder from being
/// needlessly suffixed just because a preset run claimed the same name elsewhere.</item>
/// </list>
///
/// The seed is taken once per directory, before the job writes anything into it, so the separator's
/// own pre-rename output files are never mistaken for pre-existing occupants. Reservation is
/// case-insensitive because the output directory may live on a case-insensitive filesystem
/// (Windows/macOS).
/// </summary>
public sealed class OutputNamer
{
    // Claimed base names per directory, both keys case-folded: paths and file names are
    // case-insensitive on Windows and macOS, and treating them otherwise would miss collisions.
    private readonly Dictionary<string, HashSet<string>> _claimedByDirectory = new(
        StringComparer.OrdinalIgnoreCase
    );

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
    /// Reserves <paramref name="baseName"/> in <paramref name="directory"/> and returns the name to
    /// actually use: the requested name if free, otherwise the same name with the smallest unused
    /// " (n)" suffix (starting at 2). A name is free only when no earlier reservation and no file
    /// already on disk in that directory holds it. The returned name is recorded as claimed so
    /// subsequent reservations of the same base name receive the next suffix. Deterministic: the
    /// suffix is a function only of the directory's starting contents and how many equal names were
    /// reserved before it, never of time or randomness.
    /// </summary>
    public string Reserve(string directory, string baseName)
    {
        var claimed = ClaimsFor(directory);

        if (claimed.Add(baseName))
            return baseName;

        for (int n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (claimed.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Gives up a name previously returned by <see cref="Reserve"/>, so it is available again. Used
    /// when the write the reservation was made for did not happen: holding the claim would push
    /// every later stem with that name onto a needless " (n)" suffix for a file that is not there.
    /// </summary>
    public void Release(string directory, string name) => ClaimsFor(directory).Remove(name);

    /// <summary>
    /// Convenience that builds a name (template or clean default) and reserves it in one step.
    /// </summary>
    public string ResolveAndReserve(
        string directory,
        string? template,
        string title,
        string stem,
        string presetName
    ) => Reserve(directory, BuildName(template, title, stem, presetName));

    /// <summary>
    /// Takes the snapshot of a directory's existing files now rather than on first reservation.
    /// Call this before the job writes anything into <paramref name="directory"/>: the separator
    /// writes its own pre-rename output files there, and a snapshot taken after that would treat
    /// them as pre-existing occupants. Safe to call more than once; only the first takes effect.
    /// </summary>
    public void Seed(string directory) => ClaimsFor(directory);

    /// <summary>
    /// The claim set for one directory, seeded on first use from the base names of the files already
    /// in it so an earlier job's output is treated as occupied rather than overwritten. An
    /// unreadable or not-yet-created directory simply starts empty: naming must never be the reason
    /// a job fails, and the worst case is the collision behaviour that existed before seeding.
    /// </summary>
    private HashSet<string> ClaimsFor(string directory)
    {
        if (_claimedByDirectory.TryGetValue(directory, out var existing))
            return existing;

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(directory))
                foreach (var file in Directory.EnumerateFiles(directory))
                    claimed.Add(Path.GetFileNameWithoutExtension(file));
        }
        catch (Exception)
        {
            // Unreadable directory: fall through with an empty set.
        }

        _claimedByDirectory[directory] = claimed;
        return claimed;
    }

    internal static string Sanitize(string name) =>
        string.Concat(name.Select(c => _invalidFileNameChars.Contains(c) ? '-' : c)).Trim();
}

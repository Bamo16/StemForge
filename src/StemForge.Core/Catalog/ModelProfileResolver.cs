namespace StemForge.Core.Catalog;

/// <summary>
/// Resolves a <see cref="ModelInfo"/> into a <see cref="ModelProfile"/> — its output stems with a
/// confidence/source and a composite flag — WITHOUT running the model. Tiers are tried in
/// descending order of confidence; the first that yields stems wins:
///
/// <list type="number">
///   <item>The model's own config (roformer / MDXC instrument list). The bundled benchmark data
///         already carries this for known models, so it is read straight off <see cref="ModelInfo.Stems"/>;
///         when absent for a config-driven architecture, <see cref="IModelConfigSource"/> is asked
///         to fetch the config on demand (config only, never the weights).</item>
///   <item>Architecture defaults: Demucs is a fixed four-stem set; MDX and VR are two-stem
///         (a target plus its complement).</item>
///   <item>A filename-derived target as a last resort (target only, no complement).</item>
/// </list>
///
/// Anything that survives all three tiers with no stems is reported UNKNOWN rather than failing.
/// The result is ADVISORY (ADR 0010): it informs the UI but never blocks a separation.
/// </summary>
public sealed class ModelProfileResolver(IModelConfigSource? configSource = null)
{
    private readonly IModelConfigSource? _configSource = configSource;

    /// <summary>The four stems every Demucs v4 model emits.</summary>
    private static readonly string[] DemucsStems = ["vocals", "drums", "bass", "other"];

    /// <summary>Architectures whose stems come from a per-model config (and so can be fetched on demand).</summary>
    private static bool IsConfigDriven(string architecture) =>
        architecture.Equals("MDXC", StringComparison.OrdinalIgnoreCase);

    private static bool IsDemucs(string architecture) =>
        architecture.StartsWith("Demucs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves <paramref name="model"/> to a profile. Only reaches the network (via
    /// <see cref="IModelConfigSource"/>) when a config-driven model has no benchmark stems and no
    /// cheaper tier can answer.
    /// </summary>
    public async Task<ModelProfile> ResolveAsync(ModelInfo model, CancellationToken ct = default)
    {
        var isComposite = IsCompositeModel(model.Filename);

        // ── Tier 1: config / benchmark instrument list ──────────────────────────
        // Known models already carry their config-derived stem names off the bundled benchmark
        // data; trust those at the highest confidence.
        if (model.Stems.Count > 0)
        {
            var stems = model
                .Stems.Select(s => new ProfileStem(s.Name, StemSource.Config))
                .ToList();
            return new ModelProfile(model.Filename, model.Architecture, stems, isComposite);
        }

        // No bundled stems. For a config-driven architecture, the config can still be fetched on
        // demand (config ONLY) before we fall back to a coarser tier.
        if (IsConfigDriven(model.Architecture) && _configSource is not null)
        {
            var configStems = await _configSource
                .TryGetConfigStemsAsync(model, ct)
                .ConfigureAwait(false);
            if (configStems is { Count: > 0 })
            {
                var stems = configStems.Select(n => new ProfileStem(n, StemSource.Config)).ToList();
                return new ModelProfile(model.Filename, model.Architecture, stems, isComposite);
            }
        }

        // ── Tier 2: architecture defaults ───────────────────────────────────────
        var defaults = ResolveArchitectureDefault(model);
        if (defaults.Count > 0)
            return new ModelProfile(model.Filename, model.Architecture, defaults, isComposite);

        // ── Tier 3: filename-derived target (last resort) ───────────────────────
        var target = ResolveFilenameTarget(model.Filename);
        if (target is not null)
        {
            return new ModelProfile(
                model.Filename,
                model.Architecture,
                [new ProfileStem(target, StemSource.FilenameTarget)],
                isComposite
            );
        }

        // ── Unknown ─────────────────────────────────────────────────────────────
        return new ModelProfile(model.Filename, model.Architecture, [], isComposite);
    }

    /// <summary>
    /// Architecture-default tier. Demucs is the fixed four-stem set; MDX and VR are two-stem
    /// (target + complement). The target is inferred from the filename so the complement can be
    /// named; when no target can be inferred there is no default and the resolver falls through.
    /// </summary>
    private static IReadOnlyList<ProfileStem> ResolveArchitectureDefault(ModelInfo model)
    {
        if (IsDemucs(model.Architecture))
        {
            return DemucsStems
                .Select(n => new ProfileStem(n, StemSource.ArchitectureDefault))
                .ToList();
        }

        // MDX / VR (and any other two-stem architecture): a target plus its complement.
        if (
            model.Architecture.Equals("MDX", StringComparison.OrdinalIgnoreCase)
            || model.Architecture.Equals("VR", StringComparison.OrdinalIgnoreCase)
            || model.Architecture.Equals("VR Arch", StringComparison.OrdinalIgnoreCase)
        )
        {
            var target = ResolveFilenameTarget(model.Filename);
            if (target is null)
                return [];

            var complement = ComplementOf(target);
            return
            [
                new ProfileStem(target, StemSource.ArchitectureDefault),
                new ProfileStem(complement, StemSource.ArchitectureDefault),
            ];
        }

        return [];
    }

    /// <summary>
    /// Best-effort target stem from a filename. Recognises the common UVR/audio-separator naming
    /// tokens; returns null when nothing matches so the caller can report UNKNOWN.
    /// </summary>
    internal static string? ResolveFilenameTarget(string filename)
    {
        var f = filename.ToLowerInvariant();

        if (Contains(f, "vocal", "voc"))
            return "vocals";
        if (Contains(f, "instrum", "inst", "karaoke"))
            return "instrumental";
        if (Contains(f, "drum", "kick", "snare"))
            return "drums";
        if (Contains(f, "bass"))
            return "bass";
        if (Contains(f, "guitar"))
            return "guitar";
        if (Contains(f, "piano"))
            return "piano";
        if (Contains(f, "crowd"))
            return "crowd";
        if (Contains(f, "reverb", "deecho", "echo", "denoise", "noise"))
            return "no reverb";

        return null;
    }

    /// <summary>The complementary stem produced opposite a two-stem model's target.</summary>
    private static string ComplementOf(string target) =>
        target switch
        {
            "vocals" => "instrumental",
            "instrumental" => "vocals",
            "no reverb" => "reverb",
            _ => $"no {target}",
        };

    /// <summary>
    /// Whether a weight file is a composite ("bag") model — internally several sub-models. Demucs
    /// fine-tuned / bag variants are the canonical case (e.g. <c>htdemucs_ft</c>, <c>htdemucs_6s</c>).
    /// </summary>
    internal static bool IsCompositeModel(string filename)
    {
        var f = filename.ToLowerInvariant();
        return f.Contains("_ft") || f.Contains("_6s") || f.Contains("htdemucs_ft");
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));
}

namespace StemForge.Core.Catalog;

/// <summary>A drum-extraction model offered in Settings: identity plus whether it is already
/// downloaded. <see cref="IsLocal"/> distinguishes a model present on disk from one that will be
/// fetched on first use.</summary>
public sealed record DrumModelOption(
    string Filename,
    string FriendlyName,
    string Architecture,
    bool IsLocal
);

/// <summary>
/// Lists the catalog models known to emit a "drums" stem, for the drum-extraction picker (issue #70).
/// Membership is decided per model from its <see cref="ModelProfile"/>: a model is offered only when
/// the profile resolves a "drums" stem. Models with UNKNOWN stems are intentionally excluded — the
/// picker exists so a user cannot type a non-existent or non-drums model, and surfacing the large set
/// of unknown-stem models alongside a handful of real options would be worse than free text.
///
/// The "does this model produce drums" rule is the objective part and lives in
/// <see cref="EmitsDrums"/> so it can be unit-tested directly against a profile.
/// </summary>
public sealed class DrumModelCatalog(ModelCatalogService catalog, ModelProfileResolver profiles)
{
    private readonly ModelCatalogService _catalog = catalog;
    private readonly ModelProfileResolver _profiles = profiles;

    /// <summary>The stem name a drum-extraction model must emit to qualify.</summary>
    public const string DrumsStem = "drums";

    /// <summary>
    /// True when <paramref name="profile"/> resolves a "drums" stem from any confidence tier. An
    /// UNKNOWN profile (no resolved stems) is never a drum model. This is the testable filter.
    /// </summary>
    public static bool EmitsDrums(ModelProfile profile) =>
        !profile.IsUnknown
        && profile.Stems.Any(s => s.Name.Equals(DrumsStem, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves every catalog model's profile and returns only those that emit a "drums" stem, each
    /// tagged with whether it is already on disk in <paramref name="modelsDirectory"/>. Ordered with
    /// downloaded models first, then alphabetically by friendly name.
    /// </summary>
    public async Task<IReadOnlyList<DrumModelOption>> ListAsync(
        string modelsDirectory,
        bool forceRefresh = false,
        CancellationToken ct = default
    )
    {
        var models = await _catalog.ListModelsAsync(forceRefresh, ct).ConfigureAwait(false);
        var options = new List<DrumModelOption>();

        foreach (var model in models)
        {
            ct.ThrowIfCancellationRequested();
            var profile = await _profiles.ResolveAsync(model, ct).ConfigureAwait(false);
            if (!EmitsDrums(profile))
                continue;

            var isLocal = File.Exists(Path.Combine(modelsDirectory, model.Filename));
            options.Add(
                new DrumModelOption(model.Filename, model.FriendlyName, model.Architecture, isLocal)
            );
        }

        return
        [
            .. options
                .OrderByDescending(o => o.IsLocal)
                .ThenBy(o => o.FriendlyName, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

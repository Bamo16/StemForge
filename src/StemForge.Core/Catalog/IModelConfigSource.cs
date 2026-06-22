namespace StemForge.Core.Catalog;

/// <summary>
/// Seam for fetching a model's own config on demand — the config ONLY, never the weights. Used by
/// <see cref="ModelProfileResolver"/> for the highest-confidence tier when a config-driven model
/// (roformer / MDXC) has no stems already attached from the bundled benchmark data. Implementations
/// may hit the network; the resolver only calls this when no cheaper tier can answer, so it stays
/// lazy. A null result means "config not available", letting the resolver fall through to the
/// architecture default.
/// </summary>
public interface IModelConfigSource
{
    /// <summary>
    /// Returns the instrument / stem list declared in <paramref name="model"/>'s config, or null
    /// when the config cannot be fetched or declares no stems. Never downloads the weight file.
    /// </summary>
    Task<IReadOnlyList<string>?> TryGetConfigStemsAsync(
        ModelInfo model,
        CancellationToken ct = default
    );
}

namespace StemForge.Tests.TestDoubles;

/// <summary>
/// Test double for <see cref="IModelConfigSource"/>. Returns a canned stem list (or null) and
/// records how many times it was asked, so tests can assert config-on-demand fetching happens only
/// when the cheaper tiers cannot answer. Never touches the network.
/// </summary>
public sealed class FakeModelConfigSource(IReadOnlyList<string>? stems) : IModelConfigSource
{
    private readonly IReadOnlyList<string>? _stems = stems;

    /// <summary>Number of times the resolver asked for a config — the network-fetch count.</summary>
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<string>?> TryGetConfigStemsAsync(
        ModelInfo model,
        CancellationToken ct = default
    )
    {
        CallCount++;
        return Task.FromResult(_stems);
    }
}

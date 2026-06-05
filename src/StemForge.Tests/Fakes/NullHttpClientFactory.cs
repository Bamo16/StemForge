namespace StemForge.Tests.Fakes;

/// <summary>
/// A no-op <see cref="IHttpClientFactory"/> that returns a bare <see cref="HttpClient"/> for use
/// in tests that construct services requiring the factory but never exercise HTTP calls.
/// </summary>
internal sealed class NullHttpClientFactory : IHttpClientFactory
{
    public static readonly NullHttpClientFactory Instance = new();

    public HttpClient CreateClient(string name) => new();
}

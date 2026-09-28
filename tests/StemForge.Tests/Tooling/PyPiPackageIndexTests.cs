using System.Net;
using System.Text;

namespace StemForge.Tests.Tooling;

public sealed class PyPiPackageIndexTests
{
    [Fact]
    public async Task LatestVersionAsync_ReadsInfoVersion()
    {
        var index = new PyPiPackageIndex(
            new StubFactory(
                HttpStatusCode.OK,
                """{ "info": { "version": "0.47.0", "requires_python": ">=3.10" }, "releases": {} }"""
            )
        );

        var version = await index.LatestVersionAsync(
            "audio-separator",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("0.47.0", version);
    }

    [Fact]
    public async Task LatestVersionAsync_OnHttpError_ReturnsNull()
    {
        var index = new PyPiPackageIndex(new StubFactory(HttpStatusCode.NotFound, "{}"));

        var version = await index.LatestVersionAsync(
            "audio-separator",
            TestContext.Current.CancellationToken
        );

        Assert.Null(version);
    }

    private sealed class StubFactory(HttpStatusCode status, string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(status, body));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
    }
}

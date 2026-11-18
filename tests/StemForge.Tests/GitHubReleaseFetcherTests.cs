using System.Net;
using System.Text;

namespace StemForge.Tests;

public sealed class GitHubReleaseFetcherTests
{
    [Fact]
    public async Task FetchLatestTagAsync_ReleaseJson_ReturnsTagNameFromTheLatestReleaseUrl()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"tag_name":"v1.4.2","name":"x"}""");

        var tag = await Fetcher(handler).FetchLatestTagAsync(TestContext.Current.CancellationToken);

        Assert.Equal("v1.4.2", tag);
        Assert.Equal(
            "https://api.github.com/repos/Bamo16/StemForge/releases/latest",
            handler.RequestUri?.ToString()
        );
    }

    [Fact]
    public async Task FetchLatestTagAsync_NoTagField_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"name":"x"}""");

        Assert.Null(
            await Fetcher(handler).FetchLatestTagAsync(TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.Forbidden, """{"tag_name":"v9"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task FetchLatestTagAsync_FailureOrGarbage_ReturnsNullInsteadOfThrowing(
        HttpStatusCode status,
        string body
    ) =>
        Assert.Null(
            await Fetcher(new StubHandler(status, body))
                .FetchLatestTagAsync(TestContext.Current.CancellationToken)
        );

    private static GitHubReleaseFetcher Fetcher(StubHandler handler) => new(new Factory(handler));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private sealed class Factory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == "github"
                ? new HttpClient(handler)
                : throw new InvalidOperationException($"Unexpected client {name}");
    }
}

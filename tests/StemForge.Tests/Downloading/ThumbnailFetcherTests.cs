using System.Net;

namespace StemForge.Tests.Downloading;

public sealed class ThumbnailFetcherTests : IDisposable
{
    private static readonly byte[] Image = [1, 2, 3, 4, 5];

    private readonly string _dir = Directory.CreateTempSubdirectory("sftests-thumb-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("https://i.ytimg.com/vi/abc/maxresdefault.jpg", "thumbnail.jpg")]
    [InlineData("https://i.ytimg.com/vi_webp/abc/maxresdefault.webp?sqp=1&rs=2", "thumbnail.webp")]
    [InlineData("https://example.com/art.png", "thumbnail.png")]
    public async Task DownloadAsync_NamesTheFileAfterTheUrlPathExtension(string url, string name)
    {
        var path = await Fetcher(HttpStatusCode.OK)
            .DownloadAsync(url, _dir, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_dir, name), path);
        Assert.NotNull(path);
        Assert.Equal(
            Image,
            await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task DownloadAsync_UrlWithoutExtension_DefaultsToJpg()
    {
        var path = await Fetcher(HttpStatusCode.OK)
            .DownloadAsync(
                "https://example.com/image/12345",
                _dir,
                TestContext.Current.CancellationToken
            );

        Assert.Equal(Path.Combine(_dir, "thumbnail.jpg"), path);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadAsync_BlankUrl_ReturnsNullAndWritesNothing(string? url)
    {
        var path = await Fetcher(HttpStatusCode.OK)
            .DownloadAsync(url, _dir, TestContext.Current.CancellationToken);

        Assert.Null(path);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task DownloadAsync_ServerError_ReturnsNullAndWritesNothing()
    {
        var path = await Fetcher(HttpStatusCode.NotFound)
            .DownloadAsync(
                "https://example.com/a.jpg",
                _dir,
                TestContext.Current.CancellationToken
            );

        Assert.Null(path);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task DownloadAsync_NotAUrl_ReturnsNullInsteadOfThrowing() =>
        Assert.Null(
            await Fetcher(HttpStatusCode.OK)
                .DownloadAsync("not a url", _dir, TestContext.Current.CancellationToken)
        );

    private static ThumbnailFetcher Fetcher(HttpStatusCode status) =>
        new(new Factory(new Handler(status)));

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new ByteArrayContent(Image) }
            );
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == "thumbnail"
                ? new HttpClient(handler)
                : throw new InvalidOperationException($"Unexpected client {name}");
    }
}

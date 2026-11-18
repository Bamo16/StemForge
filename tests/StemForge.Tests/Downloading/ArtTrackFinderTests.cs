using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace StemForge.Tests.Downloading;

public sealed class ArtTrackFinderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "InnerTube", name));

    private static ArtTrackFinder FinderFor(RoutingHandler handler) => new(new Factory(handler));

    [Fact]
    public async Task FindAsync_RecordedResponses_ReturnsTheArtTrackAndPostsBothQueries()
    {
        var handler = new RoutingHandler
        {
            ["next"] = Fixture("next-streetpop.json"),
            ["search"] = Fixture("search-streetpop.json"),
        };

        var pick = await FinderFor(handler)
            .FindAsync("-pj24dQYGRc", TestContext.Current.CancellationToken);

        Assert.Equal(
            new ArtTrackSuggestion("LcTV5ceBb_I", "STREETPOP (feat. RedDash)", "Wävė", 200, 0),
            pick
        );
        Assert.Equal(["next", "search"], handler.Requests.Select(request => request.Endpoint));
        Assert.Equal("-pj24dQYGRc", handler.Requests[0].Body["videoId"]?.GetValue<string>());
        Assert.Equal(
            "RDAMVM-pj24dQYGRc",
            handler.Requests[0].Body["playlistId"]?.GetValue<string>()
        );
        Assert.Contains("Wävė", handler.Requests[1].Body["query"]?.GetValue<string>());
    }

    [Fact]
    public async Task FindAsync_VideoThatIsAlreadyTheSong_ReturnsNullWithoutSearching()
    {
        var next = JsonNode
            .Parse(Fixture("next-streetpop.json"))!
            .ToJsonString()
            .Replace("MUSIC_VIDEO_TYPE_UGC", "MUSIC_VIDEO_TYPE_ATV");
        var handler = new RoutingHandler { ["next"] = next, ["search"] = "{}" };

        var pick = await FinderFor(handler)
            .FindAsync("-pj24dQYGRc", TestContext.Current.CancellationToken);

        Assert.Null(pick);
        Assert.Equal(["next"], handler.Requests.Select(request => request.Endpoint));
    }

    [Fact]
    public async Task FindAsync_UnreadableWatchResponse_ReturnsNullWithoutSearching()
    {
        var handler = new RoutingHandler { ["next"] = "{}", ["search"] = "{}" };

        var pick = await FinderFor(handler).FindAsync("abc", TestContext.Current.CancellationToken);

        Assert.Null(pick);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FindAsync_ServerError_ThrowsHttpRequestException()
    {
        var handler = new RoutingHandler { Status = HttpStatusCode.ServiceUnavailable };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            FinderFor(handler).FindAsync("abc", TestContext.Current.CancellationToken)
        );
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _bodies = [];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<(string Endpoint, JsonObject Body)> Requests { get; } = [];

        public string this[string endpoint]
        {
            set => _bodies[endpoint] = value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var endpoint = request.RequestUri!.Segments[^1];
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            Requests.Add((endpoint, body));

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(
                    _bodies.GetValueOrDefault(endpoint, "{}"),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class Factory(RoutingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == ArtTrackFinder.HttpClientName
                ? new HttpClient(handler) { BaseAddress = new Uri("https://music.youtube.com/") }
                : throw new InvalidOperationException($"Unexpected client {name}");
    }
}

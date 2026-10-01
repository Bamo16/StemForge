using System.Net;

namespace StemForge.Tests.Catalog;

public sealed class ModelConfigSourceTests : IDisposable
{
    private const string SwConfig = "BS-Roformer-SW.yaml";

    private static readonly ModelInfo Sw = new(
        "BS-Roformer-SW.ckpt",
        "MDXC",
        "Roformer Model: BS Roformer SW by jarredou",
        [],
        ["BS-Roformer-SW.ckpt", SwConfig]
    );

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"stemforge-config-{Guid.NewGuid():N}"
    );

    private string ModelsDir => Path.Combine(_root, "models");
    private string CacheDir => Path.Combine(_root, "cache");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── Parsing ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseInstruments_FlowList_ReturnsLowercasedStems()
    {
        const string yaml = """
            training:
              instruments: ['bass', 'drums', "Vocals"]
              target_instrument: null
            """;

        Assert.Equal(["bass", "drums", "vocals"], ModelConfigSource.ParseInstruments(yaml));
    }

    [Fact]
    public void ParseInstruments_BlockList_StopsAtTheNextKey()
    {
        const string yaml =
            "training:\r\n  instruments:\r\n    - Vocals\r\n    - Instrumental\r\n  target_instrument: Vocals\r\n";

        Assert.Equal(["vocals", "instrumental"], ModelConfigSource.ParseInstruments(yaml));
    }

    [Fact]
    public void ParseInstruments_NoInstrumentsKey_ReturnsNull()
    {
        Assert.Null(ModelConfigSource.ParseInstruments("audio:\n  chunk_size: 588800\n"));
    }

    // ── Lookup ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TryGetConfigStems_ConfigInModelsDirectory_ReadsItWithoutTheNetwork()
    {
        Directory.CreateDirectory(ModelsDir);
        File.WriteAllText(Path.Combine(ModelsDir, SwConfig), "  instruments: ['bass', 'drums']");
        var http = new RecordingHandler(_ => throw new InvalidOperationException("no network"));

        var stems = await NewSource(http).TryGetConfigStemsAsync(Sw, Ct);

        Assert.Equal(["bass", "drums"], stems);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task TryGetConfigStems_MissingFromUvr_FallsBackToAudioSeparatorAndCaches()
    {
        var http = new RecordingHandler(uri =>
            uri.Host == "github.com" && uri.AbsolutePath.Contains("/TRvlvr/")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("  instruments: ['drums', 'piano']"),
                }
        );

        var stems = await NewSource(http).TryGetConfigStemsAsync(Sw, Ct);

        Assert.Equal(["drums", "piano"], stems);
        Assert.Collection(
            http.Requests,
            uri => Assert.Contains("/TRvlvr/", uri.AbsolutePath),
            uri => Assert.Contains("/nomadkaraoke/python-audio-separator/", uri.AbsolutePath)
        );

        // A later session reads the cached copy instead of fetching again.
        var offline = new RecordingHandler(_ => throw new InvalidOperationException("no network"));
        Assert.Equal(["drums", "piano"], await NewSource(offline).TryGetConfigStemsAsync(Sw, Ct));
        Assert.Empty(offline.Requests);
    }

    [Fact]
    public async Task TryGetConfigStems_VipModel_FetchesFromTheVipRepository()
    {
        var vip = Sw with { FriendlyName = "MDX23C Model VIP: Something" };
        var http = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await NewSource(http).TryGetConfigStemsAsync(vip, Ct));
        Assert.Contains("/Anjok0109/ai_magic/", http.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task TryGetConfigStems_NetworkFailure_StopsFetchingForTheSession()
    {
        var http = new RecordingHandler(_ => throw new HttpRequestException("offline"));
        var source = NewSource(http);
        var other = Sw with { Filename = "other.ckpt", Files = ["other.ckpt", "other.yaml"] };

        Assert.Null(await source.TryGetConfigStemsAsync(Sw, Ct));
        Assert.Null(await source.TryGetConfigStemsAsync(other, Ct));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task TryGetConfigStems_ModelWithoutAConfig_ReturnsNull()
    {
        var onnx = Sw with { Files = ["UVR-MDX-NET-Inst_HQ_5.onnx"] };
        var http = new RecordingHandler(_ => throw new InvalidOperationException("no network"));

        Assert.Null(await NewSource(http).TryGetConfigStemsAsync(onnx, Ct));
        Assert.Empty(http.Requests);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ModelConfigSource NewSource(RecordingHandler http) =>
        new(
            new HandlerFactory(http),
            new AppPaths(new AppSettings { ModelsDirectory = ModelsDir }),
            CacheDir
        );

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(Func<Uri, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!);

            return Task.FromResult(respond(request.RequestUri!));
        }
    }
}

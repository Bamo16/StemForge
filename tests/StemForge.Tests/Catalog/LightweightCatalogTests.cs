using StemForge.Tests.TestDoubles;

namespace StemForge.Tests.Catalog;

public sealed class LightweightCatalogTests : IDisposable
{
    private readonly string _script = Path.Combine(
        Path.GetTempPath(),
        $"sftests-{Guid.NewGuid():N}.py"
    );

    private static AppPaths Paths() => new(new AppSettings(), PlatformInfo.Current);

    public void Dispose() => File.Delete(_script);

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("log line\n{\"a\":{\"b\":2}}\ntrailing", "{\"a\":{\"b\":2}}")]
    [InlineData("noise {x} {\"a\":1} more {y}", "{x} {\"a\":1} more {y}")]
    public void ExtractJsonObject_CarvesFromFirstOpenToLastCloseBrace(
        string raw,
        string expected
    ) => Assert.Equal(expected, LightweightCatalog.ExtractJsonObject(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no braces here")]
    [InlineData("} reversed {")]
    [InlineData("{")]
    [InlineData("}")]
    public void ExtractJsonObject_NoObject_IsNull(string? raw) =>
        Assert.Null(LightweightCatalog.ExtractJsonObject(raw));

    [Fact]
    public async Task RunScriptAsync_MissingScript_ReturnsEmptyAndRunsNothing()
    {
        var runner = new FakeProcessRunner();

        var output = await LightweightCatalog.RunScriptAsync(
            runner,
            Paths(),
            _script,
            [],
            "test",
            CancellationToken.None
        );

        Assert.Equal(string.Empty, output);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RunScriptAsync_RunsScriptThenArgsViaTheSeparatorPython()
    {
        File.WriteAllText(_script, string.Empty);
        var paths = Paths();
        var runner = new FakeProcessRunner();
        runner.Setup(paths.SeparationDriverPython, "{\"ok\":true}");

        var output = await LightweightCatalog.RunScriptAsync(
            runner,
            paths,
            _script,
            ["--flag", "value"],
            "test",
            CancellationToken.None
        );

        var call = Assert.Single(runner.Calls);
        Assert.Equal(paths.SeparationDriverPython, call.Exe);
        Assert.Equal([_script, "--flag", "value"], call.Args);
        Assert.Equal("{\"ok\":true}", output);
    }

    [Fact]
    public async Task RunScriptAsync_EmptyStdout_FallsBackToCombinedOutput()
    {
        File.WriteAllText(_script, string.Empty);
        var paths = Paths();
        var runner = new FakeProcessRunner();
        runner.Setup(
            paths.SeparationDriverPython,
            new FakeProcessRunner.FakeResult(0, "  ", "{\"from\":\"stderr\"}")
        );

        var output = await LightweightCatalog.RunScriptAsync(
            runner,
            paths,
            _script,
            [],
            "test",
            CancellationToken.None
        );

        Assert.Contains("{\"from\":\"stderr\"}", output);
    }
}

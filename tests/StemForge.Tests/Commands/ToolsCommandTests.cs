using System.Text.Json;
using StemForge.Cli.Json;

namespace StemForge.Tests.Commands;

public sealed class ToolsCommandTests
{
    [Fact]
    public void Json_ReportedTools_AreCamelCaseWithEveryFieldPresent()
    {
        ToolReport[] tools =
        [
            new("ffmpeg", @"C:\bin\ffmpeg.exe", true, false, "9.0", "v1.0.42"),
            new("deno", @"C:\bin\deno.exe", false, true, null, null),
        ];

        using var json = JsonDocument.Parse(CliJson.Serialize(tools));

        var first = json.RootElement[0];
        Assert.Equal(
            ["kind", "path", "installed", "overridden", "version", "release"],
            first.EnumerateObject().Select(p => p.Name)
        );
        Assert.Equal("ffmpeg", first.GetProperty("kind").GetString());
        Assert.True(first.GetProperty("installed").GetBoolean());
        Assert.False(first.GetProperty("overridden").GetBoolean());
        Assert.Equal("v1.0.42", first.GetProperty("release").GetString());

        var second = json.RootElement[1];
        Assert.False(second.GetProperty("installed").GetBoolean());
        Assert.True(second.GetProperty("overridden").GetBoolean());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("release").ValueKind);
    }
}

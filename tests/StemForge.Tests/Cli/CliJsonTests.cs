using StemForge.Cli.Json;

namespace StemForge.Tests.Cli;

public sealed class CliJsonTests
{
    private sealed record Payload(string SongTitle, int TrackCount, string? Missing);

    [Fact]
    public void Serialize_PropertyNamesAreCamelCase() =>
        Assert.Equal(
            """{"songTitle":"Hi","trackCount":3,"missing":null}""",
            CliJson.Serialize(new Payload("Hi", 3, null))
        );

    [Fact]
    public void Write_PrintsTheSerializedValueAsTheOnlyLineOnStdout()
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            CliJson.Write(new Payload("Hi", 3, "x"));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(
            """{"songTitle":"Hi","trackCount":3,"missing":"x"}""" + Environment.NewLine,
            writer.ToString()
        );
    }
}

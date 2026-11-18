using System.Text.Json;

namespace StemForge.Tests.Separation;

public sealed class DriverEventTests
{
    private static DriverEvent? Parse(string line) =>
        JsonSerializer.Deserialize(line, DriverJsonContext.Default.DriverEvent);

    [Fact]
    public void Deserialize_PhaseLine_BindsSnakeCaseFieldsAndTheEnumMemberName()
    {
        var evt = Parse(
            """{"event":"phase","phase":"downloading_model","model":"m.ckpt","model_index":1,"model_count":3,"cached":false}"""
        );

        Assert.Equal(
            new PhaseEvent
            {
                Phase = DriverPhase.DownloadingModel,
                Model = "m.ckpt",
                ModelIndex = 1,
                ModelCount = 3,
                Cached = false,
            },
            evt
        );
    }

    [Fact]
    public void Deserialize_ReadyLine_BindsDeviceAndVersion() =>
        Assert.Equal(
            new ReadyEvent { Device = "cuda", SeparatorVersion = "0.41.1" },
            Parse("""{"event":"ready","device":"cuda","separator_version":"0.41.1"}""")
        );

    [Fact]
    public void Deserialize_ProgressLine_BindsCounts() =>
        Assert.Equal(
            new ProgressEvent
            {
                Current = 4,
                Total = 10,
                Final = true,
            },
            Parse("""{"event":"progress","current":4,"total":10,"final":true}""")
        );

    [Fact]
    public void Deserialize_JobCompletedLine_BindsOutputsAndDiscarded()
    {
        var evt = Assert.IsType<JobCompletedEvent>(
            Parse(
                """{"event":"job_completed","outputs":[{"stem":"vocals","path":"/o/v.flac"}],"discarded":[{"stem":"other","path":"/o/o.flac"}],"duration_seconds":12.5}"""
            )
        );

        Assert.Equal("vocals", Assert.Single(evt.Outputs!).Stem);
        Assert.Equal("/o/v.flac", evt.Outputs![0].Path);
        Assert.Equal("/o/o.flac", Assert.Single(evt.Discarded!).Path);
        Assert.Equal(12.5, evt.DurationSeconds);
    }

    [Fact]
    public void Deserialize_JobFailedLine_BindsErrorAndTraceback() =>
        Assert.Equal(
            new JobFailedEvent { Error = "boom", Traceback = "tb" },
            Parse("""{"event":"job_failed","error":"boom","traceback":"tb"}""")
        );

    [Theory]
    [InlineData("job_cancelled", typeof(JobCancelledEvent))]
    [InlineData("bye", typeof(ByeEvent))]
    [InlineData("log", typeof(LogEvent))]
    [InlineData("stem_written", typeof(StemWrittenEvent))]
    [InlineData("error", typeof(ErrorEvent))]
    public void Deserialize_DiscriminatorPicksTheDerivedRecord(string name, Type expected) =>
        Assert.IsType(expected, Parse($$"""{"event":"{{name}}"}"""));

    [Fact]
    public void Deserialize_UnknownEvent_Throws() =>
        Assert.ThrowsAny<JsonException>(() => Parse("""{"event":"mystery"}"""));

    [Fact]
    public void Deserialize_UnknownPhase_Throws() =>
        Assert.ThrowsAny<JsonException>(() => Parse("""{"event":"phase","phase":"daydreaming"}"""));
}

namespace StemForge.Tests.Separation;

public sealed class JobRecordTests
{
    private const string Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    [Theory]
    [InlineData(1, false, false, "Vocals")]
    [InlineData(2, false, false, "2 presets")]
    [InlineData(1, true, false, "Vocals + Drums")]
    [InlineData(2, true, false, "2 presets + Drums")]
    [InlineData(1, false, true, "Vocals + Source")]
    [InlineData(2, true, true, "2 presets + Drums + Source")]
    [InlineData(0, true, false, "Drums only")]
    [InlineData(0, false, true, "Source only")]
    [InlineData(0, true, true, "Drums + Source")]
    public void OutputSummary_NamesWhatTheJobWrites(
        int presets,
        bool drums,
        bool source,
        string expected
    )
    {
        var record = Build(presets, drums, source, Url);

        Assert.Equal(expected, record.OutputSummary);
    }

    [Fact]
    public void OutputSummary_LocalFile_IgnoresKeepSource()
    {
        var record = Build(presets: 1, drums: false, source: true, url: null);

        Assert.Equal("Vocals", record.OutputSummary);
    }

    [Theory]
    [InlineData(0, false, true, Url, true)]
    [InlineData(0, true, true, Url, false)]
    [InlineData(1, false, true, Url, false)]
    [InlineData(0, false, false, Url, false)]
    [InlineData(0, false, true, null, false)]
    public void IsSourceOnly_OnlyForAUrlJobWritingNothingButTheSource(
        int presets,
        bool drums,
        bool source,
        string? url,
        bool expected
    ) => Assert.Equal(expected, Build(presets, drums, source, url).IsSourceOnly);

    private static JobRecord Build(int presets, bool drums, bool source, string? url) =>
        new(
            Id: Guid.NewGuid(),
            InputFilePath: url is null ? @"C:\audio\track.flac" : null,
            SourceUrl: url,
            Presets:
            [
                .. new[]
                {
                    MakePreset("vocals", "Vocals"),
                    MakePreset("inst", "Instrumental"),
                }.Take(presets),
            ],
            OutputDir: @"C:\output",
            ModelsDir: @"C:\models",
            KeepSourceFile: source,
            ExtractDrums: drums
        );

    private static Preset MakePreset(string id, string label) =>
        new(
            Id: id,
            Label: label,
            Category: PresetCategory.Vocals,
            Description: "",
            ModelCount: 1,
            Vram: ""
        );
}

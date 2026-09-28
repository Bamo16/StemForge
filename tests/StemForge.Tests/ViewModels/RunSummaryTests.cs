using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class RunSummaryTests
{
    [Fact]
    public void PresetsOnly_KeepsTodaysWording()
    {
        var summary = Summary(presets: 2, models: 4);

        Assert.Equal(" presets selected", summary.Headline);
        Assert.Equal("4 model runs", summary.Detail);
        Assert.Equal("▶  Run", summary.PrimaryActionLabel);
        Assert.False(summary.IsHeadlineStandalone);
    }

    [Fact]
    public void PresetsWithExtras_NameTheExtras()
    {
        var summary = Summary(presets: 1, models: 2, drums: true, source: true);

        Assert.Equal(" preset + drum stem + source", summary.Headline);
        Assert.Equal("3 model runs", summary.Detail);
    }

    [Fact]
    public void DrumsOnlyFromAFile_IsOneModelRun()
    {
        var summary = Summary(drums: true, input: RunInput.File);

        Assert.Equal("Drum stem only", summary.Headline);
        Assert.Equal("1 model run", summary.Detail);
        Assert.Equal("▶  Run", summary.PrimaryActionLabel);
        Assert.True(summary.IsHeadlineStandalone);
    }

    [Fact]
    public void DrumsAndSourceFromAUrl()
    {
        var summary = Summary(drums: true, source: true);

        Assert.Equal("Drum stem + source", summary.Headline);
        Assert.Equal("1 model run", summary.Detail);
    }

    [Fact]
    public void SourceOnly_IsADownload()
    {
        var summary = Summary(source: true);

        Assert.True(summary.IsSourceOnly);
        Assert.Equal("Source audio only", summary.Headline);
        Assert.Equal("Download only · no separation", summary.Detail);
        Assert.Equal("↓  Download", summary.PrimaryActionLabel);
    }

    [Fact]
    public void SourceTickedForAFile_CountsForNothing()
    {
        var summary = Summary(source: true, input: RunInput.File);

        Assert.False(summary.HasOutput);
        Assert.Equal("Nothing to write", summary.Headline);
        Assert.Equal("Pick a preset or Drum stem", summary.Detail);
    }

    [Theory]
    [InlineData(RunInput.Url, true, "Pick a preset, Drum stem or Source")]
    [InlineData(RunInput.None, true, "Pick a preset, Drum stem or Source")]
    [InlineData(RunInput.Url, false, "Tick Source audio to download")]
    public void Nothing_SaysWhatToPick(RunInput input, bool canSeparate, string expected)
    {
        var summary = Summary(input: input, canSeparate: canSeparate);

        Assert.False(summary.HasOutput);
        Assert.Equal(expected, summary.Detail);
    }

    [Fact]
    public void WithoutAudioSeparator_PresetsAndDrumsCountForNothing()
    {
        var summary = Summary(presets: 2, models: 4, drums: true, canSeparate: false);

        Assert.Equal(0, summary.Presets);
        Assert.False(summary.Drums);
        Assert.False(summary.HasOutput);
        Assert.Equal("Tick Source audio to download", summary.Detail);
    }

    private static RunSummary Summary(
        int presets = 0,
        int models = 0,
        bool drums = false,
        bool source = false,
        RunInput input = RunInput.Url,
        bool canSeparate = true
    ) => new(presets, models, drums, source, input, canSeparate);
}

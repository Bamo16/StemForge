using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class FormatPickerItemTests
{
    private static FormatPickerItem Build(YtDlpFormat format) =>
        new()
        {
            Format = format,
            Codec = "opus",
            Bitrate = "128k",
            SampleRate = "48 kHz",
        };

    [Fact]
    public void FormatId_IsTheWrappedFormatsId() =>
        Assert.Equal("251", Build(new YtDlpFormat { FormatId = "251" }).FormatId);

    [Fact]
    public void FormatId_WhenTheFormatHasNone_IsEmptyNotNull() =>
        Assert.Equal(string.Empty, Build(new YtDlpFormat()).FormatId);

    [Fact]
    public void NewItem_IsNotSelectedOrRecommended()
    {
        var item = Build(new YtDlpFormat { FormatId = "251" });

        Assert.False(item.IsSelected);
        Assert.False(item.IsAutoRecommended);
        Assert.False(item.IsYtPremium);
        Assert.Equal(string.Empty, item.FormatNote);
    }
}

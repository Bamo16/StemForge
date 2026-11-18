using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class DrumModelOptionViewModelTests
{
    private static DrumModelOptionViewModel Build(bool isLocal, string? note = null) =>
        new(new DrumModelOption("kit.ckpt", "Kit Splitter", "mdx23c", isLocal, note));

    [Fact]
    public void StateLabel_LocalModel_SaysDownloaded() =>
        Assert.Equal("Downloaded", Build(isLocal: true).StateLabel);

    [Fact]
    public void StateLabel_ModelFetchedOnFirstRun_SaysFetchedOnUse() =>
        Assert.Equal("Fetched on use", Build(isLocal: false).StateLabel);

    [Fact]
    public void ToString_IsTheFriendlyNameSoTheComboBoxShowsIt() =>
        Assert.Equal("Kit Splitter", Build(isLocal: true).ToString());

    [Fact]
    public void Properties_PassTheWrappedOptionThrough()
    {
        var vm = Build(isLocal: true, note: "Great hats");

        Assert.Equal("kit.ckpt", vm.Filename);
        Assert.Equal("Kit Splitter", vm.FriendlyName);
        Assert.Equal("mdx23c", vm.Architecture);
        Assert.True(vm.IsLocal);
        Assert.Equal("Great hats", vm.Note);
    }
}

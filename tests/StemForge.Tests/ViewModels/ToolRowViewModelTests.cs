using System.ComponentModel;
using System.Windows.Input;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class ToolRowViewModelTests
{
    private sealed class StubPicker : IVariantPicker
    {
        public bool IsCpu => true;
        public bool IsCuda => false;
        public bool IsDirectML => false;
        public bool HasCpuVariant => true;
        public bool HasCudaVariant => false;
        public bool HasDirectMLVariant => false;
        public string GpuHint => string.Empty;
        public ICommand SetVariantCommand => throw new NotSupportedException();

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }
    }

    private static ToolRowViewModel Build(
        string? downloadSize = "120 MB",
        bool withPicker = false
    ) =>
        new(
            ToolCatalog.Get(ToolKind.AudioSeparator) with
            {
                DownloadSize = downloadSize,
            },
            withPicker ? new StubPicker() : null
        );

    [Fact]
    public void HasDownloadSize_NoSize_IsFalse() =>
        Assert.False(Build(downloadSize: null).HasDownloadSize);

    [Fact]
    public void HasDownloadSize_BlankSize_IsFalse() =>
        Assert.False(Build(downloadSize: "  ").HasDownloadSize);

    [Fact]
    public void HasDownloadSize_RealSize_IsTrue() => Assert.True(Build().HasDownloadSize);

    [Fact]
    public void Name_IsTheToolsCliName()
    {
        var tool = ToolCatalog.Get(ToolKind.Ytdlp);

        Assert.Equal(tool.CliName, new ToolRowViewModel(tool).Name);
    }

    [Fact]
    public void Outcome_FoundAndInstalledThisRun_ShowsInstalledNowOnly()
    {
        var row = Build();

        row.Found = true;
        row.InstallSucceeded = true;

        Assert.True(row.ShowInstalledNow);
        Assert.False(row.ShowAlreadyInstalled);
    }

    [Fact]
    public void Outcome_FoundBeforeTheWizardInstalledAnything_ShowsAlreadyInstalledOnly()
    {
        var row = Build();

        row.Found = true;

        Assert.False(row.ShowInstalledNow);
        Assert.True(row.ShowAlreadyInstalled);
    }

    [Fact]
    public void Outcome_NotFound_ShowsNeither()
    {
        var row = Build();

        row.InstallSucceeded = true;

        Assert.False(row.ShowInstalledNow);
        Assert.False(row.ShowAlreadyInstalled);
    }

    [Fact]
    public void ShowVariantPicker_WithoutAPicker_IsAlwaysFalse()
    {
        var row = Build();

        row.WantInstall = true;

        Assert.False(row.ShowVariantPicker);
    }

    [Fact]
    public void ShowVariantPicker_QueuedAndIdle_IsTrue()
    {
        var row = Build(withPicker: true);

        row.WantInstall = true;

        Assert.True(row.ShowVariantPicker);
    }

    [Fact]
    public void ShowVariantPicker_NotQueued_IsFalse() =>
        Assert.False(Build(withPicker: true).ShowVariantPicker);

    [Fact]
    public void ShowVariantPicker_WhileInstalling_IsFalse()
    {
        var row = Build(withPicker: true);
        row.WantInstall = true;

        row.IsInstalling = true;

        Assert.False(row.ShowVariantPicker);
    }

    [Fact]
    public void ShowVariantPicker_ToolAlreadyFound_IsFalse()
    {
        var row = Build(withPicker: true);
        row.WantInstall = true;

        row.Found = true;

        Assert.False(row.ShowVariantPicker);
    }

    [Fact]
    public void ShowVariantPicker_ChangeIsRaisedWhenAnInputChanges()
    {
        var row = Build(withPicker: true);
        List<string?> raised = [];
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.WantInstall = true;
        row.IsInstalling = true;
        row.Found = true;

        Assert.Equal(3, raised.Count(name => name == nameof(ToolRowViewModel.ShowVariantPicker)));
    }

    [Fact]
    public void Outcome_ChangeIsRaisedForBothFlagsWhenInstallSucceededChanges()
    {
        var row = Build();
        List<string?> raised = [];
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.InstallSucceeded = true;

        Assert.Contains(nameof(ToolRowViewModel.ShowInstalledNow), raised);
        Assert.Contains(nameof(ToolRowViewModel.ShowAlreadyInstalled), raised);
    }
}

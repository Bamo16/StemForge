using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class SeparateViewModelTests
{
    [Fact]
    public void ExpandPath_TildeSlashSubdir_ExpandsToUserProfileSubdir()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.Combine(userProfile, "Music");

        var result = SeparateViewModel.ExpandPath("~/Music");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExpandPath_AbsolutePath_ReturnedUnchanged()
    {
        const string absolute = @"C:\Users\TestUser\Music\Stems";

        var result = SeparateViewModel.ExpandPath(absolute);

        Assert.Equal(absolute, result);
    }

    [Fact]
    public void ExpandPath_TildeAlone_ExpandsToUserProfileFolder()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var result = SeparateViewModel.ExpandPath("~");

        Assert.Equal(expected, result);
    }
}

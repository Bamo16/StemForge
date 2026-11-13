namespace StemForge.Tests.Catalog;

public sealed class CheckpointNameTests
{
    [Fact]
    public void TryParse_EpochAndSdrInFilename_ReadsBoth()
    {
        var parsed = CheckpointName.TryParse(
            "model_bs_roformer_ep_317_sdr_12.9755.ckpt",
            out var name
        );

        Assert.True(parsed);
        Assert.NotNull(name);
        Assert.Equal(317, name.Epoch);
        Assert.Equal(12.9755, name.Sdr);
    }

    [Theory]
    [InlineData("1_HP-UVR.pth")]
    [InlineData("htdemucs_ft.yaml")]
    [InlineData("model_ep_x_sdr_1.ckpt")]
    public void TryParse_FilenameWithoutTheConvention_IsFalse(string filename) =>
        Assert.False(CheckpointName.TryParse(filename, out _));
}

using StemForge.Cli.Commands;

namespace StemForge.Tests.Commands;

public sealed class SetupCommandTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("suggested")]
    [InlineData("SUGGESTED")]
    [InlineData("cpu")]
    [InlineData("CUDA")]
    [InlineData("DirectML")]
    public void Validate_AcceptableVariant_Succeeds(string? variant) =>
        Assert.True(
            new SetupCommand.Settings { Variant = variant }
                .Validate()
                .Successful
        );

    [Theory]
    [InlineData("opencl")]
    [InlineData("")]
    [InlineData("gpu")]
    public void Validate_UnknownVariant_FailsNamingTheValidChoices(string variant)
    {
        var result = new SetupCommand.Settings { Variant = variant }.Validate();

        Assert.False(result.Successful);
        Assert.Equal("--variant must be suggested, cpu, cuda or directml.", result.Message);
    }
}

using System.Globalization;
using StemForge.Converters;

namespace StemForge.Tests.Converters;

public sealed class RampOpacityConverterTests
{
    private static double Ramp(double percent, object? parameter = null) =>
        (double)
            RampOpacityConverter.Instance.Convert(
                percent,
                typeof(double),
                parameter,
                CultureInfo.InvariantCulture
            );

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(11.0, 0.5)]
    [InlineData(22.0, 1.0)]
    [InlineData(50.0, 1.0)]
    [InlineData(100.0, 1.0)]
    public void Ramps_to_full_opacity_at_threshold(double percent, double expected)
    {
        Assert.Equal(expected, Ramp(percent, "22"), precision: 3);
    }

    [Fact]
    public void Uses_default_threshold_when_parameter_missing()
    {
        // Default threshold is 22, so 11 percent maps to half opacity.
        Assert.Equal(0.5, Ramp(11.0), precision: 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Nonpositive_threshold_yields_full_opacity(string threshold)
    {
        Assert.Equal(1.0, Ramp(5.0, threshold), precision: 3);
    }

    [Fact]
    public void Clamps_negative_percent_to_zero()
    {
        Assert.Equal(0.0, Ramp(-10.0, "22"), precision: 3);
    }
}

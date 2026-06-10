using System.Globalization;
using Avalonia.Data.Converters;

namespace StemForge.Converters;

/// <summary>
/// Maps a 0-100 fill percentage to an opacity that ramps linearly from 0 up to 1, reaching full
/// opacity at a threshold percentage and holding 1 above it. The threshold comes from the
/// ConverterParameter (a number, default 22).
///
/// Used by the ProgressBar shimmer so the highlight fades in as the bar fills. On a nearly empty
/// bar the indicator is only a few pixels wide and the shimmer band covers all of it, which reads
/// as the whole fill flashing white rather than a travelling sweep; ramping the overlay opacity
/// to zero at low fill suppresses that flash and lets the sweep appear once the bar is wide enough.
/// </summary>
public sealed class RampOpacityConverter : IValueConverter
{
    public static readonly RampOpacityConverter Instance = new();

    private const double DefaultThreshold = 22.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            _ => 0.0,
        };

        var threshold = parameter switch
        {
            double d => d,
            string s
                when double.TryParse(
                    s,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed
                ) => parsed,
            _ => DefaultThreshold,
        };

        if (threshold <= 0)
            return 1.0;

        return Math.Clamp(percent / threshold, 0.0, 1.0);
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

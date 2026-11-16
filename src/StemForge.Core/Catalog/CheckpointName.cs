using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StemForge.Core.Catalog;

/// <summary>The epoch and validation SDR a checkpoint's author put in its filename.</summary>
public sealed partial record CheckpointName(int Epoch, double Sdr)
{
    [GeneratedRegex(
        @"ep_(?<Epoch>\d+)_sdr_(?<Sdr>\d+(\.\d+)?)",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
    )]
    private static partial Regex Pattern { get; }

    public static bool TryParse(string filename, [NotNullWhen(true)] out CheckpointName? name)
    {
        name = null;
        if (
            Pattern.Match(filename) is not { Success: true } match
            || !int.TryParse(match.Groups["Epoch"].Value, out var epoch)
            || !double.TryParse(
                match.Groups["Sdr"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sdr
            )
        )
            return false;

        name = new CheckpointName(epoch, sdr);

        return true;
    }
}

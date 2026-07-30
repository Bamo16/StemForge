using System.Collections.Frozen;

namespace StemForge.Core.Separation;

/// <summary>
/// A known ensemble algorithm: its catalog key, a short human label for chips,
/// and a longer description for tooltips.
/// </summary>
public sealed record EnsembleAlgorithmInfo(string Key, string Label, string Description);

/// <summary>
/// Single source of truth for ensemble-algorithm metadata, shared by the custom-ensemble
/// picker (Models view) and the preset-card algorithm chip (Separate view).
///
/// Keys mirror audio-separator's <c>ensemble_algorithm</c> values. Algorithms operate either
/// in the waveform domain (<c>avg_wave</c>, <c>median_wave</c>, <c>min_wave</c>, <c>max_wave</c>)
/// or the spectral/FFT-magnitude domain (<c>avg_fft</c>, <c>median_fft</c>, <c>min_fft</c>,
/// <c>max_fft</c>). audio-separator emits <c>avg_fft</c> for the mean-spectral blend; the
/// Models picker historically offered <c>mean_fft</c> for the same algorithm, so both keys
/// resolve to the same entry.
/// </summary>
public static class EnsembleAlgorithmCatalog
{
    static EnsembleAlgorithmCatalog()
    {
        var definitions = new (EnsembleAlgorithmInfo Info, string[] Aliases)[]
        {
            (
                new(
                    "avg_wave",
                    "Averaged",
                    "Average the waveforms of all models. Simple, reliable general-purpose blend."
                ),
                []
            ),
            (
                new(
                    "median_wave",
                    "Median",
                    "Median of waveforms across models. More robust to outlier models than averaging."
                ),
                []
            ),
            (
                new(
                    "min_wave",
                    "Min wave",
                    "Minimum waveform amplitude at each sample. Aggressively suppresses anything not shared by all models."
                ),
                []
            ),
            (
                new(
                    "max_wave",
                    "Max wave",
                    "Maximum waveform amplitude at each sample. Maximises captured signal across models."
                ),
                []
            ),
            (
                new(
                    "avg_fft",
                    "Mean FFT",
                    "Mean spectral magnitude. Smoother frequency-domain blend than avg_wave."
                ),
                ["mean_fft"] // audio-separator emits avg_fft; the Models picker historically used mean_fft.
            ),
            (
                new(
                    "median_fft",
                    "Median FFT",
                    "Median spectral magnitude. Robust frequency-domain blend, good when one model is significantly noisier than the others."
                ),
                []
            ),
            (
                new(
                    "min_fft",
                    "Min FFT",
                    "Minimum spectral magnitude at each frequency. Aggressively suppresses noise at the cost of detail."
                ),
                []
            ),
            (
                new(
                    "max_fft",
                    "Max FFT",
                    "Maximum spectral magnitude at each frequency. Maximises loudness and recovered detail."
                ),
                []
            ),
            (
                new(
                    "uvr_max_spec",
                    "Max spectrum",
                    "UVR maximum-spectrum blend. Keeps the loudest spectral content across models, maximising detail and fullness."
                ),
                []
            ),
            (
                new(
                    "uvr_min_spec",
                    "Min spectrum",
                    "UVR minimum-spectrum blend. Keeps the quietest spectral content across models, suppressing anything not shared by all of them."
                ),
                []
            ),
        };

        _byKey = definitions
            .SelectMany(algo =>
                algo.Aliases.Prepend(algo.Info.Key)
                    .Select(key => KeyValuePair.Create(key, algo.Info))
            )
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Known = [.. definitions.Select(d => d.Info)];
    }

    private static readonly FrozenDictionary<string, EnsembleAlgorithmInfo> _byKey;

    /// <summary>Known algorithms in display order, for the custom-ensemble picker.</summary>
    public static IReadOnlyList<EnsembleAlgorithmInfo> Known { get; }

    /// <summary>
    /// Resolves an algorithm key (canonical or alias) to its metadata. An unknown key yields
    /// an entry whose label and description are the raw key, so the chip renders without error.
    /// </summary>
    public static EnsembleAlgorithmInfo Resolve(string? key)
    {
        if (key?.Trim() is not { Length: > 0 } trimmed)
            return new EnsembleAlgorithmInfo(string.Empty, string.Empty, string.Empty);

        return _byKey.TryGetValue(trimmed, out var algo)
            ? algo
            : new EnsembleAlgorithmInfo(trimmed, trimmed, trimmed);
    }
}

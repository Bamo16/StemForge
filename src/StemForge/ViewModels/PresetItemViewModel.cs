using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.ViewModels;

public partial class PresetItemViewModel(Preset preset) : ObservableObject
{
    public Preset Preset { get; } = preset;

    public string Id => Preset.Id;
    public string Label => Preset.Label;
    public string Description => Preset.Description;
    public string ModelsTag =>
        Preset is { ModelCount: > 0 and var count } ? "model".ToQuantity(count) : string.Empty;
    public string VramTag => Preset.Vram;
    public bool HasVramTag => !string.IsNullOrEmpty(Preset.Vram);

    // ── Ensemble algorithm chip (presets that run two or more models) ──────────
    private EnsembleAlgorithmInfo Algorithm =>
        EnsembleAlgorithmCatalog.Resolve(Preset.EnsembleAlgorithm);

    /// <summary>True only for ensembles (2+ models) that carry a known-or-raw algorithm key.</summary>
    public bool HasAlgorithmTag =>
        Preset.ModelCount >= 2 && !string.IsNullOrWhiteSpace(Preset.EnsembleAlgorithm);

    /// <summary>Short label shown on the chip (human label for known keys, raw key otherwise).</summary>
    public string AlgorithmTag => Algorithm.Label;

    /// <summary>Tooltip text describing the algorithm.</summary>
    public string AlgorithmTooltip => Algorithm.Description;

    public IReadOnlyList<string> ModelsList => Preset.AllModels;
    public bool HasModelsList => Preset.AllModels.Count > 0;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

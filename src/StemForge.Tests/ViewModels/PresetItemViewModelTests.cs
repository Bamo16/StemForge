using StemForge.Core.Models;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class PresetItemViewModelTests
{
    private static Preset Ensemble(string? algorithm, int modelCount = 2) =>
        new(
            Id: "vocal_full",
            Label: "Full",
            Category: PresetCategory.Vocals,
            Description: "desc",
            ModelCount: modelCount,
            Vram: string.Empty,
            Models: ["a.ckpt", "b.ckpt"],
            EnsembleAlgorithm: algorithm
        );

    [Fact]
    public void EnsembleWithKnownAlgorithm_ShowsChipWithLabelAndTooltip()
    {
        var vm = new PresetItemViewModel(Ensemble("max_fft"));

        Assert.True(vm.HasAlgorithmTag);
        Assert.Equal("Max FFT", vm.AlgorithmTag);
        Assert.NotEmpty(vm.AlgorithmTooltip);
    }

    [Fact]
    public void EnsembleWithUnknownAlgorithm_ShowsRawKeyChip()
    {
        var vm = new PresetItemViewModel(Ensemble("future_algo"));

        Assert.True(vm.HasAlgorithmTag);
        Assert.Equal("future_algo", vm.AlgorithmTag);
        Assert.Equal("future_algo", vm.AlgorithmTooltip);
    }

    [Fact]
    public void EnsembleWithoutAlgorithm_HidesChip()
    {
        var vm = new PresetItemViewModel(Ensemble(algorithm: null));

        Assert.False(vm.HasAlgorithmTag);
    }

    [Fact]
    public void SingleModelPreset_HidesChip()
    {
        var preset = new Preset(
            Id: "solo",
            Label: "Solo",
            Category: PresetCategory.Other,
            Description: "desc",
            ModelCount: 1,
            Vram: string.Empty,
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "a.ckpt",
            EnsembleAlgorithm: "max_fft" // even if an algorithm is set, single-model never shows it
        );

        var vm = new PresetItemViewModel(preset);

        Assert.False(vm.HasAlgorithmTag);
    }

    [Fact]
    public void ModelCountBelowTwo_HidesChip()
    {
        var vm = new PresetItemViewModel(Ensemble("max_fft", modelCount: 1));
        Assert.False(vm.HasAlgorithmTag);
    }
}

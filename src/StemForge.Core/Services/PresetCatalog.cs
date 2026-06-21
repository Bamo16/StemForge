using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>
/// Fallback built-in preset catalog, used before the torch-free <c>list_presets.py</c> one-shot
/// resolves the live catalog. Data matches <c>audio_separator/ensemble_presets.json</c>.
/// </summary>
public sealed class PresetCatalog
{
    public static IReadOnlyList<Preset> BuiltIn { get; } =
    [
        new(
            "vocal_balanced",
            "Balanced",
            PresetCategory.Vocals,
            "Best overall vocal quality — Resurrection (SDR 11.34) + Beta 6X (SDR 11.12) averaged",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "bs_roformer_vocals_resurrection_unwa.ckpt",
                "melband_roformer_big_beta6x.ckpt",
            ],
            EnsembleAlgorithm: "avg_fft"
        ),
        new(
            "vocal_clean",
            "Clean",
            PresetCategory.Vocals,
            "Minimal instrument bleed in vocals — Revive 2 (bleedless 40.07) + FT2 bleedless (39.30) with min FFT",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "bs_roformer_vocals_revive_v2_unwa.ckpt",
                "mel_band_roformer_kim_ft2_bleedless_unwa.ckpt",
            ],
            EnsembleAlgorithm: "min_fft"
        ),
        new(
            "vocal_full",
            "Full",
            PresetCategory.Vocals,
            "Maximum vocal capture including harmonies — Revive 3e (fullness 21.43) + becruily vocal with max FFT",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "bs_roformer_vocals_revive_v3e_unwa.ckpt",
                "mel_band_roformer_vocals_becruily.ckpt",
            ],
            EnsembleAlgorithm: "max_fft"
        ),
        new(
            "vocal_rvc",
            "RVC",
            PresetCategory.Vocals,
            "Optimized for RVC/AI voice training data — Beta 6X + Gabox voc_fv4 averaged",
            ModelCount: 2,
            Vram: "",
            Models: ["melband_roformer_big_beta6x.ckpt", "mel_band_roformer_vocals_fv4_gabox.ckpt"],
            EnsembleAlgorithm: "avg_wave"
        ),
        new(
            "instrumental_balanced",
            "Balanced",
            PresetCategory.Instrumentals,
            "Good balance of noise and fullness — Gabox INSTV8 + Resurrection Inst",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "mel_band_roformer_instrumental_instv8_gabox.ckpt",
                "bs_roformer_instrumental_resurrection_unwa.ckpt",
            ],
            EnsembleAlgorithm: "uvr_max_spec"
        ),
        new(
            "instrumental_clean",
            "Clean",
            PresetCategory.Instrumentals,
            "Cleanest instrumentals with minimal vocal bleed — Fv7z (bleedless 44.61) + Resurrection Inst (SDR 17.25)",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "mel_band_roformer_instrumental_fv7z_gabox.ckpt",
                "bs_roformer_instrumental_resurrection_unwa.ckpt",
            ],
            EnsembleAlgorithm: "uvr_max_spec"
        ),
        new(
            "instrumental_full",
            "Full",
            PresetCategory.Instrumentals,
            "Maximum instrument preservation — v1e+ (fullness 37.89) + becruily inst (SOTA SDR 17.55)",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "melband_roformer_inst_v1e_plus.ckpt",
                "mel_band_roformer_instrumental_becruily.ckpt",
            ],
            EnsembleAlgorithm: "uvr_max_spec"
        ),
        new(
            "instrumental_low_resource",
            "Low Resource",
            PresetCategory.Instrumentals,
            "Fast ensemble for low VRAM — Resurrection Inst (200MB) + MDX HQ_5 (ONNX, very fast)",
            ModelCount: 2,
            Vram: "",
            Models:
            [
                "bs_roformer_instrumental_resurrection_unwa.ckpt",
                "UVR-MDX-NET-Inst_HQ_5.onnx",
            ],
            EnsembleAlgorithm: "avg_fft"
        ),
        new(
            "karaoke",
            "Karaoke",
            PresetCategory.Instrumentals,
            "Lead vocal removal — 3-model karaoke ensemble reaches SDR ~10.6 vs ~10.2 single model",
            ModelCount: 3,
            Vram: "",
            Models:
            [
                "mel_band_roformer_karaoke_aufr33_viperx_sdr_10.1956.ckpt",
                "mel_band_roformer_karaoke_gabox_v2.ckpt",
                "mel_band_roformer_karaoke_becruily.ckpt",
            ],
            EnsembleAlgorithm: "avg_wave"
        ),
    ];
}

using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class ModelDetailsTests
{
    private static readonly ModelInfo Viperx = new(
        "model_bs_roformer_ep_317_sdr_12.9755.ckpt",
        "MDXC",
        "Roformer Model: BS-Roformer-Viperx-1297",
        [new StemSdr("vocals", 12.9755), new StemSdr("instrumental", null)],
        ["model_bs_roformer_ep_317_sdr_12.9755.ckpt"],
        TargetStem: "vocals",
        SecondsPerMinute: 8.6
    );

    [Fact]
    public void Stems_MarkOnlyTheTargetAndFormatSdr()
    {
        var details = new ModelDetails(Viperx, null, [], null);

        Assert.Equal(
            [
                new StemDetail("vocals", "13.0 dB", true),
                new StemDetail("instrumental", string.Empty, false),
            ],
            details.Stems
        );
    }

    [Fact]
    public void Speed_And_Training_AreSpelledOut()
    {
        var details = new ModelDetails(Viperx, null, [], null);

        Assert.Equal(
            "8.6 seconds per minute of audio, measured on the benchmark machine",
            details.Speed
        );
        Assert.Equal("Epoch 317, author's validation SDR 12.98", details.Training);
    }

    [Fact]
    public void Speed_And_Training_AreNullWhenUnknown()
    {
        var model = new ModelInfo("1_HP-UVR.pth", "VR", "VR Arch: HP", [], ["1_HP-UVR.pth"]);

        var details = new ModelDetails(model, null, [], null);

        Assert.Null(details.Speed);
        Assert.Null(details.Training);
        Assert.False(details.HasStems);
    }

    [Fact]
    public void Stems_NoBenchmark_FallBackToProfileWithoutSdr()
    {
        var model = new ModelInfo("x.ckpt", "MDXC", "X", [], ["x.ckpt"]);
        var profile = new ModelProfile(
            "x.ckpt",
            "MDXC",
            [new ProfileStem("drums", StemSource.Config)],
            false
        );

        var details = new ModelDetails(model, profile, [], null);

        Assert.Equal([new StemDetail("drums", string.Empty, false)], details.Stems);
    }

    [Fact]
    public void PresetsText_ListsOnlyPresetsUsingTheModel()
    {
        var presets = new[]
        {
            new Preset(
                "a",
                "Balanced",
                PresetCategory.Vocals,
                "",
                1,
                "",
                Models: [Viperx.Filename]
            ),
            new Preset("b", "Other", PresetCategory.Drums, "", 1, "", Models: ["unrelated.ckpt"]),
        };

        var details = new ModelDetails(Viperx, null, presets, null);

        Assert.Equal("Balanced (Vocals)", details.PresetsText);
    }

    [Fact]
    public void Note_ShownOnlyWhenPresent()
    {
        var note = new ModelNote(
            "Cleanest drums.",
            [new ModelNoteLink("Repo", "https://x.test")],
            null
        );

        Assert.True(new ModelDetails(Viperx, null, [], note).HasNote);
        Assert.False(new ModelDetails(Viperx, null, [], null).HasNote);
    }

    [Theory]
    [InlineData("Demucs", "dl.fbaipublicfiles.com/demucs")]
    [InlineData("MDXC", "github.com/TRvlvr/model_repo releases")]
    public void Source_FollowsArchitecture(string architecture, string label)
    {
        var model = Viperx with { Architecture = architecture };

        Assert.Equal(label, new ModelDetails(model, null, [], null).Source.Label);
    }
}

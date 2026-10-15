namespace StemForge.Tests.Catalog;

public sealed class ModelNotesTests
{
    public static TheoryData<string> Filenames => [.. ModelNotes.Load().Keys];

    [Theory]
    [MemberData(nameof(Filenames))]
    public void Note_IsWellFormed(string filename)
    {
        var note = ModelNotes.For(filename);

        Assert.NotNull(note);
        Assert.Contains('.', filename);
        Assert.False(string.IsNullOrWhiteSpace(note.Summary));
        Assert.NotEmpty(note.Links);
        Assert.All(
            note.Links,
            link =>
            {
                Assert.False(string.IsNullOrWhiteSpace(link.Label));
                Assert.True(Uri.TryCreate(link.Url, UriKind.Absolute, out var uri));
                Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            }
        );
    }

    [Fact]
    public void For_EveryDrumPickerModel_HasANote()
    {
        string[] drumModels =
        [
            "BS-Roformer-SW.ckpt",
            "htdemucs_ft.yaml",
            "htdemucs.yaml",
            "hdemucs_mmi.yaml",
            "htdemucs_6s.yaml",
            "kuielab_a_drums.onnx",
            "kuielab_b_drums.onnx",
        ];

        Assert.All(drumModels, filename => Assert.NotNull(ModelNotes.For(filename)));
    }

    [Fact]
    public void For_IgnoresCase_AndAnswersNullForAModelWithoutANote()
    {
        Assert.NotNull(ModelNotes.For("bs-roformer-sw.CKPT"));
        Assert.Null(ModelNotes.For("UVR-MDX-NET-Inst_HQ_5.onnx"));
    }
}

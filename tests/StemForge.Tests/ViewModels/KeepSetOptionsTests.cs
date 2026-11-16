using StemForge.Core.Catalog;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class KeepSetOptionsTests
{
    private static ModelProfile Profile(StemSource source, params string[] stems) =>
        new("m.ckpt", "MDXC", [.. stems.Select(stem => new ProfileStem(stem, source))], false);

    [Fact]
    public void Config_and_architecture_stems_are_offered_as_a_union()
    {
        var options = KeepSetOptions.Resolve([
            ("A", Profile(StemSource.Config, "vocals", "other")),
            ("B", Profile(StemSource.ArchitectureDefault, "Drums", "vocals")),
        ]);

        Assert.True(options.CanPick);
        Assert.Equal(["drums", "other", "vocals"], options.Stems);
        Assert.Null(options.Note);
    }

    [Fact]
    public void A_filename_guess_falls_back_to_keep_all_with_a_note_naming_the_model()
    {
        var options = KeepSetOptions.Resolve([
            ("A", Profile(StemSource.Config, "vocals", "other")),
            ("Guessed", Profile(StemSource.FilenameTarget, "vocals")),
        ]);

        Assert.False(options.CanPick);
        Assert.Contains("Guessed", options.Note);
    }

    [Fact]
    public void An_unresolved_model_falls_back_to_keep_all()
    {
        var options = KeepSetOptions.Resolve([("Mystery", null)]);

        Assert.False(options.CanPick);
        Assert.NotNull(options.Note);
    }
}

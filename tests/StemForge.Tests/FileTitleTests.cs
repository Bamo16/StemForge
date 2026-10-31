using System.Globalization;
using System.Text;

namespace StemForge.Tests;

public sealed class FileTitleTests
{
    // YouTube HbhqeBUuvwE: a long credit list and a Japanese title, 221 characters in all.
    private const string Futari =
        "Ai Maeda, Mariko Suzuki, Rumi Shishido, Kotoba no Puzzle Mojipittan, Bandai Namco Game Music, "
        + "Hiroyuki Goto (Bandai Namco Entertainment Inc.), Satoru Kosaki (Bandai Namco Entertainment Inc.) - "
        + "ふたりのぜのぴったん(『ゼノサーガフリークス』)より収録";

    private const string FutariSong = "ふたりのぜのぴったん(『ゼノサーガフリークス』)より収録";

    // An ASCII "Artist - Title" just past the limit, and one just within it.
    private static readonly string AsciiOver =
        $"{new string('a', 90)} - {new string('t', FileTitle.MaxLength - 92)}";
    private static readonly string AsciiAtLimit =
        $"{new string('a', 90)} - {new string('t', FileTitle.MaxLength - 93)}";

    private static readonly string[] Stems =
    [
        "Vocals",
        "Instrumental",
        "Drums",
        "Bass",
        "Other",
        "Guitar",
        "Piano",
        "Lead Vocals",
        "Backing Vocals",
        "Synthesizer",
        "Woodwinds",
    ];

    public static TheoryData<string> LongTitles => [Futari, AsciiOver];

    [Fact]
    public void A_title_that_fits_is_left_alone()
    {
        Assert.Equal(FileTitle.MaxLength, AsciiAtLimit.Length);
        Assert.Equal(AsciiAtLimit, FileTitle.Fit(AsciiAtLimit));
    }

    [Theory]
    [MemberData(nameof(LongTitles))]
    public void A_long_title_is_shortened_the_same_way_every_time(string title)
    {
        var fitted = FileTitle.Fit(title);

        Assert.True(fitted.Length <= FileTitle.MaxLength, $"{fitted.Length}: {fitted}");
        Assert.Equal(fitted, FileTitle.Fit(title));
        Assert.Equal(fitted, FileTitle.Fit(fitted));
        Assert.Contains('…', fitted);
        Assert.Matches(@" \[[0-9a-f]{6}\]$", fitted);
    }

    [Fact]
    public void The_credit_list_is_cut_before_the_song_title()
    {
        var fitted = FileTitle.Fit(Futari);

        Assert.StartsWith("Ai Maeda, Mariko Suzuki, Rumi Shishido", fitted);
        Assert.Contains($"… - {FutariSong} [", fitted);
    }

    [Theory]
    [MemberData(nameof(LongTitles))]
    public void Every_name_built_from_a_long_title_fits_a_file_name(string title)
    {
        var fitted = FileTitle.Fit(title);
        List<string> baseNames = [fitted];
        foreach (
            var preset in PresetCatalog.BuiltIn.Append(Preset.DrumExtraction("htdemucs_ft.yaml"))
        )
            baseNames.AddRange(
                Stems.Select(stem => SeparationPipeline.DesiredBaseName(preset, stem, fitted))
            );

        string[] extensions = [".flac", ".wav", ".mp3"];
        foreach (
            var (baseName, ext) in baseNames.SelectMany(
                name => extensions,
                (name, ext) => (name, ext)
            )
        )
        {
            // The longest form a name takes: a collision suffix, then Ableton's analysis file.
            var longest = $"{baseName} (99){ext}.asd";
            Assert.True(
                longest.Length <= FileTitle.MaxComponentLength,
                $"{longest.Length}: {longest}"
            );
        }
    }

    [Fact]
    public void A_template_that_repeats_the_title_still_fits()
    {
        var preset = new Preset(
            "mine",
            "Mine",
            PresetCategory.Vocals,
            "Test preset",
            1,
            "",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "model.ckpt",
            NameTemplate: "{title} - {preset} - {title} ({stem})"
        );

        var name = SeparationPipeline.DesiredBaseName(preset, "Vocals", FileTitle.Fit(Futari));

        Assert.True($"{name} (99).flac.asd".Length <= FileTitle.MaxComponentLength, name);
        Assert.StartsWith("Ai Maeda", name);
    }

    [Fact]
    public void Two_long_titles_that_differ_only_past_the_cut_get_different_names()
    {
        var first = FileTitle.Fit($"{AsciiOver} part 1");
        var second = FileTitle.Fit($"{AsciiOver} part 2");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("👍🏽")] // a surrogate pair and a skin-tone modifier
    [InlineData("é")] // e and a combining acute accent
    [InlineData("🇯🇵")] // a flag: two regional indicators
    public void A_title_is_never_cut_inside_a_grapheme(string grapheme)
    {
        for (var pad = 0; pad < 4; pad++)
        {
            var title = $"{new string('x', pad)}{string.Concat(Enumerable.Repeat(grapheme, 200))}";

            var fitted = FileTitle.Fit(title);
            var kept = fitted[..fitted.IndexOf('…')];

            // Strict UTF-8 throws on a lone surrogate.
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(fitted);
            Assert.Equal(0, (kept.Length - pad) % grapheme.Length);
            Assert.Equal(
                (kept.Length - pad) / grapheme.Length,
                new StringInfo(kept).LengthInTextElements - pad
            );
        }
    }

    [Fact]
    public void Without_long_paths_a_deep_folder_shortens_the_title_to_fit_MAX_PATH()
    {
        var deep = Path.Combine(Path.GetTempPath(), new string('d', 60), new string('e', 20));

        var max = FileTitle.MaxLengthIn(deep, longPathsEnabled: false);
        var fitted = FileTitle.Fit(AsciiOver, max);

        Assert.True(max < FileTitle.MaxLength);
        Assert.True(
            Path.Combine(
                Path.GetFullPath(deep),
                $"{fitted} (Instrumental - Balanced) (99).flac.asd"
            ).Length <= 259
        );
        Assert.Equal(FileTitle.MaxLength, FileTitle.MaxLengthIn(deep, longPathsEnabled: true));
    }

    [Fact]
    public void A_download_is_named_from_the_shortened_title()
    {
        var meta = new YtDlpMetadata(
            SourceUrl: "https://www.youtube.com/watch?v=HbhqeBUuvwE",
            Title: FutariSong,
            Artists: Futari[..Futari.IndexOf(" - ", StringComparison.Ordinal)].Split(", "),
            Uploader: null,
            SourceCodec: null,
            SourceBitrateKbps: null,
            DurationSeconds: null,
            FormatId: null,
            MediaUrl: string.Empty
        );

        Assert.Equal(Futari, meta.DisplayTitle);
        Assert.Equal(FileTitle.Fit(Futari), meta.BaseName);
    }
}

namespace StemForge.Tests.Separation;

/// <summary>
/// Unit tests for <see cref="OutputNamer"/>: the pure name-building and collision-disambiguation
/// logic that gives every separation run a clean, deterministic, job-unique output file name.
/// </summary>
public sealed class OutputNamerTests
{
    // ── Clean default ──────────────────────────────────────────────────────────

    [Fact]
    public void CleanName_IsTitleSpaceParenStem()
    {
        Assert.Equal("Song Title (Vocals)", OutputNamer.CleanName("Song Title", "Vocals"));
    }

    [Fact]
    public void BuildName_NullTemplate_FallsBackToCleanDefault()
    {
        Assert.Equal(
            "Song (Vocals)",
            OutputNamer.BuildName(template: null, "Song", "Vocals", "My Preset")
        );
    }

    [Fact]
    public void BuildName_EmptyOrWhitespaceTemplate_FallsBackToCleanDefault()
    {
        Assert.Equal("Song (Drums)", OutputNamer.BuildName("", "Song", "Drums", "P"));
        Assert.Equal("Song (Drums)", OutputNamer.BuildName("   ", "Song", "Drums", "P"));
    }

    // ── Template tokens ────────────────────────────────────────────────────────

    [Fact]
    public void BuildName_TitleToken_Expands()
    {
        Assert.Equal("Song", OutputNamer.BuildName("{title}", "Song", "Vocals", "P"));
    }

    [Fact]
    public void BuildName_StemToken_Expands()
    {
        Assert.Equal("Vocals", OutputNamer.BuildName("{stem}", "Song", "Vocals", "P"));
    }

    [Fact]
    public void BuildName_PresetToken_Expands()
    {
        Assert.Equal("My Preset", OutputNamer.BuildName("{preset}", "Song", "Vocals", "My Preset"));
    }

    [Fact]
    public void BuildName_AllTokensCombined_Expand()
    {
        Assert.Equal(
            "Song - Vocals [My Preset]",
            OutputNamer.BuildName("{title} - {stem} [{preset}]", "Song", "Vocals", "My Preset")
        );
    }

    [Fact]
    public void BuildName_TokensAreCaseInsensitive()
    {
        Assert.Equal(
            "Song-Vocals-P",
            OutputNamer.BuildName("{Title}-{STEM}-{Preset}", "Song", "Vocals", "P")
        );
    }

    [Fact]
    public void BuildName_UnknownToken_LeftLiteral()
    {
        Assert.Equal(
            "{artist} Vocals",
            OutputNamer.BuildName("{artist} {stem}", "S", "Vocals", "P")
        );
    }

    [Fact]
    public void BuildName_LiteralTextWithNoTokens_PassesThrough()
    {
        Assert.Equal("just text", OutputNamer.BuildName("just text", "S", "V", "P"));
    }

    [Fact]
    public void BuildName_SanitisesPathInvalidCharacters()
    {
        // A "/" in a token value would otherwise create a subdirectory; it is replaced.
        var name = OutputNamer.BuildName("{title} ({stem})", "AC/DC", "Vocals", "P");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }

    // ── Collision disambiguation ───────────────────────────────────────────────

    // A directory that does not exist, so reservations start from an empty claim set. Seeding from
    // real directory contents is covered separately below.
    private static string EmptyDir() =>
        Path.Combine(Path.GetTempPath(), "stemforge-namer-absent", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Reserve_FirstName_ReturnedUnchanged()
    {
        var namer = new OutputNamer();
        Assert.Equal("Song (Vocals)", namer.Reserve(EmptyDir(), "Song (Vocals)"));
    }

    [Fact]
    public void Reserve_CollidingNames_GetDeterministicNumericSuffixes()
    {
        var namer = new OutputNamer();
        var dir = EmptyDir();
        Assert.Equal("Song (Vocals)", namer.Reserve(dir, "Song (Vocals)"));
        Assert.Equal("Song (Vocals) (2)", namer.Reserve(dir, "Song (Vocals)"));
        Assert.Equal("Song (Vocals) (3)", namer.Reserve(dir, "Song (Vocals)"));
    }

    [Fact]
    public void Reserve_DistinctNames_DoNotInterfere()
    {
        var namer = new OutputNamer();
        var dir = EmptyDir();
        Assert.Equal("Song (Vocals)", namer.Reserve(dir, "Song (Vocals)"));
        Assert.Equal("Song (Instrumental)", namer.Reserve(dir, "Song (Instrumental)"));
        Assert.Equal("Song (Vocals) (2)", namer.Reserve(dir, "Song (Vocals)"));
    }

    [Fact]
    public void Reserve_IsCaseInsensitive()
    {
        var namer = new OutputNamer();
        var dir = EmptyDir();
        Assert.Equal("Song (Vocals)", namer.Reserve(dir, "Song (Vocals)"));
        // Same name in different casing collides (shared output dir may be case-insensitive).
        Assert.Equal("song (vocals) (2)", namer.Reserve(dir, "song (vocals)"));
    }

    [Fact]
    public void Reserve_IsDeterministic_TwoInstancesProduceIdenticalSequences()
    {
        // Determinism proof: the suffix is a function only of reservation order, never of time or
        // randomness. Two independent namers fed the same sequence produce identical results.
        var a = new OutputNamer();
        var b = new OutputNamer();
        var dir = EmptyDir();
        var input = new[] { "X", "X", "Y", "X", "Y" };

        var resultsA = input.Select(n => a.Reserve(dir, n)).ToArray();
        var resultsB = input.Select(n => b.Reserve(dir, n)).ToArray();

        Assert.Equal(resultsA, resultsB);
        Assert.Equal(new[] { "X", "X (2)", "Y", "X (3)", "Y (2)" }, resultsA);
    }

    // ── Per-directory scoping ──────────────────────────────────────────────────

    [Fact]
    public void Reserve_SameNameInDifferentDirectories_DoesNotCollide()
    {
        // The drum stem can be written to its own cache directory. A name claimed beside the stems
        // must not push it onto a " (2)" suffix, because nothing is there to collide with.
        var namer = new OutputNamer();
        Assert.Equal("Song (Drums)", namer.Reserve(EmptyDir(), "Song (Drums)"));
        Assert.Equal("Song (Drums)", namer.Reserve(EmptyDir(), "Song (Drums)"));
    }

    // ── Seeding from existing files ────────────────────────────────────────────

    [Fact]
    public void Reserve_NameHeldByAnExistingFile_IsSuffixed()
    {
        // The case that made a second job overwrite the first job's stems: the name is free as far
        // as this job's reservations go, but a file on disk already holds it.
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Song (Vocals).flac"), "");

        var namer = new OutputNamer();

        Assert.Equal("Song (Vocals) (2)", namer.Reserve(dir.Path, "Song (Vocals)"));
    }

    [Fact]
    public void Reserve_ExistingFile_MatchesRegardlessOfExtensionOrCase()
    {
        // Output format varies per job (flac/mp3/wav), so the extension cannot be part of the match;
        // the base name is what collides.
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "song (vocals).mp3"), "");

        var namer = new OutputNamer();

        Assert.Equal("Song (Vocals) (2)", namer.Reserve(dir.Path, "Song (Vocals)"));
    }

    [Fact]
    public void Seed_TakenBeforeWrites_IgnoresFilesAddedAfterwards()
    {
        // The separator writes its own pre-rename files into the output directory. Those are not
        // pre-existing occupants, so a snapshot taken up front must not see them.
        using var dir = new TempDir();
        var namer = new OutputNamer();
        namer.Seed(dir.Path);

        File.WriteAllText(Path.Combine(dir.Path, "Song (Vocals).flac"), "");

        Assert.Equal("Song (Vocals)", namer.Reserve(dir.Path, "Song (Vocals)"));
    }

    [Fact]
    public void Reserve_MissingDirectory_StartsEmptyRatherThanThrowing()
    {
        var namer = new OutputNamer();
        Assert.Equal("Song (Vocals)", namer.Reserve(EmptyDir(), "Song (Vocals)"));
    }

    // ── Release ────────────────────────────────────────────────────────────────

    [Fact]
    public void Release_FreesTheNameForReuse()
    {
        // A rename that failed leaves no file at the reserved name, so holding the claim would push
        // the next stem onto a suffix for a file that is not there.
        var namer = new OutputNamer();
        var dir = EmptyDir();

        var first = namer.Reserve(dir, "Song (Vocals)");
        namer.Release(dir, first);

        Assert.Equal("Song (Vocals)", namer.Reserve(dir, "Song (Vocals)"));
    }
}

/// <summary>A temporary directory that deletes itself, for tests that need real files on disk.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "stemforge-namer-" + Guid.NewGuid().ToString("N")
        );

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}

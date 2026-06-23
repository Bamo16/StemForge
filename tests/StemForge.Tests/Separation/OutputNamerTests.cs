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

    [Fact]
    public void Reserve_FirstName_ReturnedUnchanged()
    {
        var namer = new OutputNamer();
        Assert.Equal("Song (Vocals)", namer.Reserve("Song (Vocals)"));
    }

    [Fact]
    public void Reserve_CollidingNames_GetDeterministicNumericSuffixes()
    {
        var namer = new OutputNamer();
        Assert.Equal("Song (Vocals)", namer.Reserve("Song (Vocals)"));
        Assert.Equal("Song (Vocals) (2)", namer.Reserve("Song (Vocals)"));
        Assert.Equal("Song (Vocals) (3)", namer.Reserve("Song (Vocals)"));
    }

    [Fact]
    public void Reserve_DistinctNames_DoNotInterfere()
    {
        var namer = new OutputNamer();
        Assert.Equal("Song (Vocals)", namer.Reserve("Song (Vocals)"));
        Assert.Equal("Song (Instrumental)", namer.Reserve("Song (Instrumental)"));
        Assert.Equal("Song (Vocals) (2)", namer.Reserve("Song (Vocals)"));
    }

    [Fact]
    public void Reserve_IsCaseInsensitive()
    {
        var namer = new OutputNamer();
        Assert.Equal("Song (Vocals)", namer.Reserve("Song (Vocals)"));
        // Same name in different casing collides (shared output dir may be case-insensitive).
        Assert.Equal("song (vocals) (2)", namer.Reserve("song (vocals)"));
    }

    [Fact]
    public void Reserve_IsDeterministic_TwoInstancesProduceIdenticalSequences()
    {
        // Determinism proof: the suffix is a function only of reservation order, never of time or
        // randomness. Two independent namers fed the same sequence produce identical results.
        var a = new OutputNamer();
        var b = new OutputNamer();
        var input = new[] { "X", "X", "Y", "X", "Y" };

        var resultsA = input.Select(a.Reserve).ToArray();
        var resultsB = input.Select(b.Reserve).ToArray();

        Assert.Equal(resultsA, resultsB);
        Assert.Equal(new[] { "X", "X (2)", "Y", "X (3)", "Y (2)" }, resultsA);
    }
}

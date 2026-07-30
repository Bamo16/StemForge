namespace StemForge.Tests.Separation;

/// <summary>
/// Tests for how <see cref="SeparationPipeline"/> derives the desired output base name for each stem
/// (<see cref="SeparationPipeline.DesiredBaseName"/>) and how the job-scoped <see cref="OutputNamer"/>
/// resolves the specific same-stem collision between two built-in presets run in one job.
/// </summary>
public sealed class OutputNamingTests
{
    private const string Title = "Song Title";

    private static Preset SingleModel(string id, string label, string? template = null) =>
        new(
            Id: id,
            Label: label,
            Category: PresetCategory.Other,
            Description: "",
            ModelCount: 1,
            Vram: "",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "m.ckpt",
            NameTemplate: template
        );

    private static Preset Builtin(string id, string label, PresetCategory category) =>
        new(id, label, category, "", ModelCount: 2, Vram: "", Models: ["a.ckpt", "b.ckpt"]);

    // ── User-preset default and template ────────────────────────────────────────

    [Fact]
    public void DesiredBaseName_UserPreset_NoTemplate_UsesCleanDefault()
    {
        var preset = SingleModel("user", "My Vocals");
        Assert.Equal(
            "Song Title (Vocals)",
            SeparationPipeline.DesiredBaseName(preset, "Vocals", Title)
        );
    }

    [Fact]
    public void DesiredBaseName_UserPreset_WithTemplate_UsesTemplate()
    {
        var preset = SingleModel("user", "My Vocals", template: "{title} [{preset}] {stem}");
        Assert.Equal(
            "Song Title [My Vocals] Vocals",
            SeparationPipeline.DesiredBaseName(preset, "Vocals", Title)
        );
    }

    [Fact]
    public void DesiredBaseName_UserPreset_PresetTokenUsesDisplayName()
    {
        var preset = SingleModel("user", "My Vocals", template: "{preset}");
        // Custom-mode presets use the user label verbatim as DisplayName.
        Assert.Equal("My Vocals", SeparationPipeline.DesiredBaseName(preset, "Vocals", Title));
    }

    [Fact]
    public void Template_RoundTripsThroughPersistence()
    {
        // The template lives on the step and survives save/reload, so a saved user preset keeps its
        // naming behaviour. Exercised through the public DesiredBaseName seam after a round trip.
        var preset = SingleModel("user", "My Vocals", template: "{stem} of {title}");
        var reloaded = SaveAndReload(preset);
        Assert.Equal(
            "Vocals of Song Title",
            SeparationPipeline.DesiredBaseName(reloaded, "Vocals", Title)
        );
    }

    // ── Built-in descriptive naming ─────────────────────────────────────────────

    [Fact]
    public void DesiredBaseName_Builtin_TargetStem_KeepsDescriptiveLabel()
    {
        var preset = Builtin("vocal_balanced", "Balanced", PresetCategory.Vocals);
        Assert.Equal(
            "Song Title (Vocal - Balanced)",
            SeparationPipeline.DesiredBaseName(preset, "Vocals", Title)
        );
    }

    [Fact]
    public void DesiredBaseName_Builtin_ResidualStem_UsesCleanDefault()
    {
        // A vocal preset's non-target (residual) stem keeps the plain clean name.
        var preset = Builtin("vocal_balanced", "Balanced", PresetCategory.Vocals);
        Assert.Equal(
            "Song Title (Instrumental)",
            SeparationPipeline.DesiredBaseName(preset, "Instrumental", Title)
        );
    }

    [Fact]
    public void DesiredBaseName_Builtin_Karaoke_UsesKaraokeLabel()
    {
        var preset = Builtin("karaoke", "Karaoke", PresetCategory.Instrumentals);
        Assert.Equal(
            "Song Title (Karaoke)",
            SeparationPipeline.DesiredBaseName(preset, "Instrumental", Title)
        );
    }

    // ── The specific built-in collision ─────────────────────────────────────────

    [Fact]
    public void BuiltinResidualCollision_TwoVocalPresetsInOneJob_AreDisambiguated()
    {
        // Two vocal built-ins run in one job each emit a Vocals target (distinct descriptive names)
        // AND an Instrumental residual. Both residuals resolve to "Song Title (Instrumental)" — the
        // real same-stem collision. The shared job namer must give them distinct names.
        var balanced = Builtin("vocal_balanced", "Balanced", PresetCategory.Vocals);
        var clean = Builtin("vocal_clean", "Clean", PresetCategory.Vocals);
        var namer = new OutputNamer();

        // Run 1 (balanced): target then residual.
        var b1 = namer.Reserve(SeparationPipeline.DesiredBaseName(balanced, "Vocals", Title));
        var b2 = namer.Reserve(SeparationPipeline.DesiredBaseName(balanced, "Instrumental", Title));
        // Run 2 (clean): target then residual.
        var c1 = namer.Reserve(SeparationPipeline.DesiredBaseName(clean, "Vocals", Title));
        var c2 = namer.Reserve(SeparationPipeline.DesiredBaseName(clean, "Instrumental", Title));

        // Targets are inherently distinct (descriptive labels differ).
        Assert.Equal("Song Title (Vocal - Balanced)", b1);
        Assert.Equal("Song Title (Vocal - Clean)", c1);
        // Residuals would have collided; the second is suffixed deterministically.
        Assert.Equal("Song Title (Instrumental)", b2);
        Assert.Equal("Song Title (Instrumental) (2)", c2);

        // No two outputs in the job share a name.
        var all = new[] { b1, b2, c1, c2 };
        Assert.Equal(all.Length, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void BuiltinTargetCollision_TwoInstrumentalResiduals_AreDisambiguated()
    {
        // Symmetric case: two instrumental built-ins each emit a Vocals residual → collision.
        var balanced = Builtin("instrumental_balanced", "Balanced", PresetCategory.Instrumentals);
        var clean = Builtin("instrumental_clean", "Clean", PresetCategory.Instrumentals);
        var namer = new OutputNamer();

        var r1 = namer.Reserve(SeparationPipeline.DesiredBaseName(balanced, "Vocals", Title));
        var r2 = namer.Reserve(SeparationPipeline.DesiredBaseName(clean, "Vocals", Title));

        Assert.Equal("Song Title (Vocals)", r1);
        Assert.Equal("Song Title (Vocals) (2)", r2);
    }

    private static Preset SaveAndReload(Preset preset)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemforge-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var roaming = Path.Combine(dir, "user_presets.json");
        var legacy = Path.Combine(dir, "legacy.json");
        try
        {
            new UserPresetService(roaming).Add(preset);
            var svc = UserPresetService.Load(roaming, legacy);
            return Assert.Single(svc.Presets);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

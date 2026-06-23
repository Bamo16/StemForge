namespace StemForge.Tests.Catalog;

/// <summary>
/// Tests for UserPresetService. Each test that touches the filesystem uses an isolated temp
/// path (via the internal path-injecting constructor / Load overload) so the suite never reads
/// or writes the real Roaming location and is safe to run in parallel.
/// </summary>
public sealed class UserPresetServiceTests
{
    [Fact]
    public void Load_FileDoesNotExist_ReturnsEmptyCollection()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            var svc = UserPresetService.Load(roaming, legacy);
            Assert.Empty(svc.Presets);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Add_IncreasesCollectionCount()
    {
        var (roaming, _, dir) = MakeTempPaths();
        try
        {
            var svc = new UserPresetService(roaming);
            svc.Add(MakePreset("p1", "Preset One"));
            Assert.Single(svc.Presets);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Add_ThenReload_RoundTripsPreset()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            var svc = new UserPresetService(roaming);
            svc.Add(MakePreset("round-trip-id", "Round Trip Preset"));

            var reloaded = UserPresetService.Load(roaming, legacy);

            Assert.Single(reloaded.Presets);
            Assert.Equal("round-trip-id", reloaded.Presets[0].Id);
            Assert.Equal("Round Trip Preset", reloaded.Presets[0].Label);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Remove_ByExistingId_ShrinksCollection()
    {
        var (roaming, _, dir) = MakeTempPaths();
        try
        {
            var svc = new UserPresetService(roaming);
            svc.Add(MakePreset("del-id", "To Delete"));
            svc.Add(MakePreset("keep-id", "To Keep"));

            svc.Remove("del-id");

            Assert.Single(svc.Presets);
            Assert.Equal("keep-id", svc.Presets[0].Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Remove_ThenReload_ConfirmsDeletion()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            var svc = new UserPresetService(roaming);
            svc.Add(MakePreset("gone-id", "Gone"));
            svc.Add(MakePreset("stay-id", "Stays"));
            svc.Remove("gone-id");

            var reloaded = UserPresetService.Load(roaming, legacy);

            Assert.Single(reloaded.Presets);
            Assert.Equal("stay-id", reloaded.Presets[0].Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmptyCollectionWithoutThrowing()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            File.WriteAllText(roaming, "{ this is not valid json }}}");

            var svc = UserPresetService.Load(roaming, legacy);

            Assert.Empty(svc.Presets);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_LegacyLocalFileExists_MigratesToRoamingAndLoadsPresets()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            // Seed a legacy (Local) presets file; no Roaming file present.
            File.WriteAllText(legacy, SerializeOne("legacy-id", "Legacy Preset"));

            var svc = UserPresetService.Load(roaming, legacy);

            Assert.Single(svc.Presets);
            Assert.Equal("legacy-id", svc.Presets[0].Id);
            Assert.Equal("Legacy Preset", svc.Presets[0].Label);
            // Migrated: now at Roaming, removed from Local (no loss, single source of truth).
            Assert.True(File.Exists(roaming));
            Assert.False(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_RoamingFileExists_DoesNotClobberWithLegacy()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            File.WriteAllText(roaming, SerializeOne("roaming-id", "Roaming Preset"));
            File.WriteAllText(legacy, SerializeOne("legacy-id", "Legacy Preset"));

            var svc = UserPresetService.Load(roaming, legacy);

            // Roaming wins (never clobbered by the legacy copy) and remains the source of truth.
            Assert.Single(svc.Presets);
            Assert.Equal("roaming-id", svc.Presets[0].Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_BothFilesExist_RemovesOrphanedLegacyAndKeepsRoamingIntact()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            // Stale state from issue #26: a Roaming file (authoritative) and a leftover Local copy
            // coexist. The Roaming content must survive untouched and the orphan must be gone.
            var roamingJson = SerializeOne("roaming-id", "Roaming Preset");
            File.WriteAllText(roaming, roamingJson);
            File.WriteAllText(legacy, SerializeOne("legacy-id", "Legacy Preset"));

            var svc = UserPresetService.Load(roaming, legacy);

            // No preset loss: Roaming content is exactly what we seeded.
            Assert.Single(svc.Presets);
            Assert.Equal("roaming-id", svc.Presets[0].Id);
            Assert.Equal(roamingJson, File.ReadAllText(roaming));
            // The two no longer coexist: the orphaned Local file is removed.
            Assert.True(File.Exists(roaming));
            Assert.False(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migrate_BothFilesExist_RemovesLegacyWithoutTouchingRoaming()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            var roamingJson = SerializeOne("roaming-id", "Roaming Preset");
            File.WriteAllText(roaming, roamingJson);
            File.WriteAllText(legacy, SerializeOne("legacy-id", "Legacy Preset"));

            UserPresetService.MigrateLegacyLocation(roaming, legacy);

            // Reconciliation deletes the orphan and leaves the authoritative Roaming file as-is.
            Assert.False(File.Exists(legacy));
            Assert.Equal(roamingJson, File.ReadAllText(roaming));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migrate_NoLegacyFile_IsSafeNoOp()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            UserPresetService.MigrateLegacyLocation(roaming, legacy);

            Assert.False(File.Exists(roaming));
            Assert.False(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migrate_RunTwice_IsIdempotent()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            File.WriteAllText(legacy, SerializeOne("legacy-id", "Legacy Preset"));

            UserPresetService.MigrateLegacyLocation(roaming, legacy);
            var migrated = File.ReadAllText(roaming);
            UserPresetService.MigrateLegacyLocation(roaming, legacy);

            Assert.False(File.Exists(legacy));
            Assert.Equal(migrated, File.ReadAllText(roaming));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Steps schema: migration from the legacy flat format ────────────────────

    [Fact]
    public void Load_LegacyFlatSingleModel_MigratesToSingleStepWithoutLoss()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            // Old v0.2.x flat schema: a single-model preset with no Steps list.
            File.WriteAllText(
                roaming,
                System.Text.Json.JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            Id = "flat-single",
                            Label = "Flat Single",
                            Category = nameof(PresetCategory.Vocals),
                            Description = "Legacy single model",
                            ModelCount = 1,
                            Vram = "4 GB",
                            Mode = nameof(SeparationMode.SingleModel),
                            PrimaryModel = "only_model.onnx",
                        },
                    }
                )
            );

            var svc = UserPresetService.Load(roaming, legacy);

            var preset = Assert.Single(svc.Presets);
            // Migrated into exactly one step whose input is the source audio.
            var step = Assert.Single(preset.Steps);
            Assert.Equal(StepInput.Source, step.Input);
            Assert.Equal(["only_model.onnx"], step.Models);
            Assert.Null(step.Algorithm);
            // Every flat field preserved and re-exposed through the flat accessors.
            Assert.Equal(SeparationMode.SingleModel, preset.Mode);
            Assert.Equal("only_model.onnx", preset.PrimaryModel);
            Assert.Equal(["only_model.onnx"], preset.AllModels);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_LegacyFlatCustomEnsemble_MigratesToSingleStepWithoutLoss()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            // Old v0.2.x flat schema: a custom ensemble (primary + extras + algorithm).
            File.WriteAllText(
                roaming,
                System.Text.Json.JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            Id = "flat-ensemble",
                            Label = "Flat Ensemble",
                            Category = nameof(PresetCategory.Instrumentals),
                            Description = "Legacy ensemble",
                            ModelCount = 3,
                            Vram = "8 GB",
                            Mode = nameof(SeparationMode.CustomEnsemble),
                            PrimaryModel = "primary.ckpt",
                            EnsembleAlgorithm = "avg_fft",
                            ExtraModels = new[] { "extra1.ckpt", "extra2.ckpt" },
                        },
                    }
                )
            );

            var svc = UserPresetService.Load(roaming, legacy);

            var preset = Assert.Single(svc.Presets);
            // All three models collapse into one ensemble step, ordered primary-first.
            var step = Assert.Single(preset.Steps);
            Assert.Equal(StepInput.Source, step.Input);
            Assert.Equal(["primary.ckpt", "extra1.ckpt", "extra2.ckpt"], step.Models);
            Assert.Equal("avg_fft", step.Algorithm);
            Assert.True(step.IsEnsemble);
            // Flat accessors reconstructed identically.
            Assert.Equal(SeparationMode.CustomEnsemble, preset.Mode);
            Assert.Equal("primary.ckpt", preset.PrimaryModel);
            Assert.Equal(["extra1.ckpt", "extra2.ckpt"], preset.ExtraModels);
            Assert.Equal("avg_fft", preset.EnsembleAlgorithm);
            Assert.Equal(["primary.ckpt", "extra1.ckpt", "extra2.ckpt"], preset.AllModels);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Steps schema: round-trip persists AS a steps list ──────────────────────

    [Fact]
    public void Save_WritesStepsList_NotFlatModelFields()
    {
        var (roaming, _, dir) = MakeTempPaths();
        try
        {
            var svc = new UserPresetService(roaming);
            svc.Add(
                new Preset(
                    Id: "ensemble-id",
                    Label: "Ensemble Preset",
                    Category: PresetCategory.Other,
                    Description: "Two models",
                    ModelCount: 2,
                    Vram: "",
                    Mode: SeparationMode.CustomEnsemble,
                    PrimaryModel: "a.ckpt",
                    EnsembleAlgorithm: "avg_wave",
                    ExtraModels: ["b.ckpt"]
                )
            );

            var json = File.ReadAllText(roaming);

            // On-disk schema is an ordered steps list, not the legacy flat fields.
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var presetElement = doc.RootElement[0];
            Assert.True(
                presetElement.TryGetProperty("Steps", out var steps),
                "Persisted preset must carry a Steps list."
            );
            Assert.Equal(1, steps.GetArrayLength());
            var firstStep = steps[0];
            Assert.Equal("Source", firstStep.GetProperty("Input").GetString());
            Assert.Equal(
                ["a.ckpt", "b.ckpt"],
                firstStep
                    .GetProperty("Models")
                    .EnumerateArray()
                    .Select(e => e.GetString())
                    .ToArray()
            );
            Assert.Equal("avg_wave", firstStep.GetProperty("Algorithm").GetString());
            // The legacy flat model fields are no longer written.
            Assert.False(presetElement.TryGetProperty("PrimaryModel", out _));
            Assert.False(presetElement.TryGetProperty("ExtraModels", out _));
            Assert.False(presetElement.TryGetProperty("Mode", out _));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Add_EnsembleThenReload_RoundTripsStepIntact()
    {
        var (roaming, legacy, dir) = MakeTempPaths();
        try
        {
            var svc = new UserPresetService(roaming);
            svc.Add(
                new Preset(
                    Id: "rt-ensemble",
                    Label: "Round Trip Ensemble",
                    Category: PresetCategory.Vocals,
                    Description: "Three models",
                    ModelCount: 3,
                    Vram: "8 GB",
                    Mode: SeparationMode.CustomEnsemble,
                    PrimaryModel: "p.ckpt",
                    EnsembleAlgorithm: "max_fft",
                    ExtraModels: ["x.ckpt", "y.ckpt"]
                )
            );

            var reloaded = UserPresetService.Load(roaming, legacy);

            var preset = Assert.Single(reloaded.Presets);
            var step = Assert.Single(preset.Steps);
            Assert.Equal(StepInput.Source, step.Input);
            Assert.Equal(["p.ckpt", "x.ckpt", "y.ckpt"], step.Models);
            Assert.Equal("max_fft", step.Algorithm);
            // Metadata and reconstructed flat shape survive intact.
            Assert.Equal("rt-ensemble", preset.Id);
            Assert.Equal(SeparationMode.CustomEnsemble, preset.Mode);
            Assert.Equal("p.ckpt", preset.PrimaryModel);
            Assert.Equal(["x.ckpt", "y.ckpt"], preset.ExtraModels);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string Roaming, string Legacy, string Dir) MakeTempPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stemforge-presets-{Guid.NewGuid():N}");
        var roamingDir = Path.Combine(dir, "roaming");
        var localDir = Path.Combine(dir, "local");
        Directory.CreateDirectory(roamingDir);
        Directory.CreateDirectory(localDir);
        return (
            Path.Combine(roamingDir, "user_presets.json"),
            Path.Combine(localDir, "user_presets.json"),
            dir
        );
    }

    private static string SerializeOne(string id, string label)
    {
        // Build the JSON shape the loader expects directly.
        return System.Text.Json.JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    Id = id,
                    Label = label,
                    Category = nameof(PresetCategory.Vocals),
                    Description = "Test preset",
                    ModelCount = 1,
                    Vram = "4 GB",
                    Mode = nameof(SeparationMode.SingleModel),
                    PrimaryModel = "test_model.onnx",
                },
            }
        );
    }

    private static Preset MakePreset(string id, string label) =>
        new(
            Id: id,
            Label: label,
            Category: PresetCategory.Vocals,
            Description: "Test preset",
            ModelCount: 1,
            Vram: "4 GB",
            Mode: SeparationMode.SingleModel,
            PrimaryModel: "test_model.onnx"
        );
}

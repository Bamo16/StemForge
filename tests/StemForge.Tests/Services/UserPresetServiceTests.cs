using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Services;

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

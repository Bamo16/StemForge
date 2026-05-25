using StemForge.Models;
using StemForge.Services;

namespace StemForge.Tests.Services;

/// <summary>
/// Tests for UserPresetService. Because the service writes to a fixed path under
/// LocalApplicationData, each test that touches the filesystem saves and restores
/// any pre-existing content in a finally block.
/// </summary>
public sealed class UserPresetServiceTests
{
    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StemForge",
            "user_presets.json"
        );

    [Fact]
    public void Load_FileDoesNotExist_ReturnsEmptyCollection()
    {
        var backup = ReadAndDelete();
        try
        {
            var svc = UserPresetService.Load();
            Assert.Empty(svc.Presets);
        }
        finally
        {
            Restore(backup);
        }
    }

    [Fact]
    public void Add_IncreasesCollectionCount()
    {
        var svc = new UserPresetService();
        svc.Add(MakePreset("p1", "Preset One"));
        Assert.Single(svc.Presets);
    }

    [Fact]
    public void Add_ThenReload_RoundTripsPreset()
    {
        var backup = ReadAndDelete();
        try
        {
            var svc = new UserPresetService();
            svc.Add(MakePreset("round-trip-id", "Round Trip Preset"));

            var reloaded = UserPresetService.Load();

            Assert.Single(reloaded.Presets);
            Assert.Equal("round-trip-id", reloaded.Presets[0].Id);
            Assert.Equal("Round Trip Preset", reloaded.Presets[0].Label);
        }
        finally
        {
            Restore(backup);
        }
    }

    [Fact]
    public void Remove_ByExistingId_ShrinksCollection()
    {
        var svc = new UserPresetService();
        svc.Add(MakePreset("del-id", "To Delete"));
        svc.Add(MakePreset("keep-id", "To Keep"));

        svc.Remove("del-id");

        Assert.Single(svc.Presets);
        Assert.Equal("keep-id", svc.Presets[0].Id);
    }

    [Fact]
    public void Remove_ThenReload_ConfirmsDeletion()
    {
        var backup = ReadAndDelete();
        try
        {
            var svc = new UserPresetService();
            svc.Add(MakePreset("gone-id", "Gone"));
            svc.Add(MakePreset("stay-id", "Stays"));
            svc.Remove("gone-id");

            var reloaded = UserPresetService.Load();

            Assert.Single(reloaded.Presets);
            Assert.Equal("stay-id", reloaded.Presets[0].Id);
        }
        finally
        {
            Restore(backup);
        }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmptyCollectionWithoutThrowing()
    {
        var backup = ReadAndDelete();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "{ this is not valid json }}}");

            var svc = UserPresetService.Load();

            Assert.Empty(svc.Presets);
        }
        finally
        {
            Restore(backup);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? ReadAndDelete()
    {
        if (!File.Exists(FilePath))
            return null;
        var content = File.ReadAllText(FilePath);
        File.Delete(FilePath);
        return content;
    }

    private static void Restore(string? content)
    {
        if (content is null)
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, content);
        }
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

using StemForge.Tests.TestDoubles;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class ModelsViewModelTests : IDisposable
{
    // Demucs needs several weight files beside its yaml; two roformers share one config.
    private const string Catalog = """
        {
          "Demucs": {
            "Demucs v4: htdemucs_ft": {
              "filename": "htdemucs_ft.yaml",
              "stems": ["vocals", "drums", "bass", "other"],
              "download_files": ["https://example.test/a.th", "https://example.test/b.th", "https://example.test/htdemucs_ft.yaml"]
            }
          },
          "MDXC": {
            "Roformer X": { "filename": "x.ckpt", "stems": ["vocals", "other"], "download_files": ["x.ckpt", "shared.yaml"] },
            "Roformer Y": { "filename": "y.ckpt", "stems": ["vocals", "other"], "download_files": ["y.ckpt", "shared.yaml"] }
          }
        }
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"stemforge-models-{Guid.NewGuid():N}"
    );

    private string ModelsDir => Path.Combine(_root, "models");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Load_DemucsModel_IsLocalOnlyWithItsWeightsAndCountsThem()
    {
        Write(("a.th", 3), ("htdemucs_ft.yaml", 5));
        var vm = await LoadAsync();

        Assert.False(Item(vm, "htdemucs_ft.yaml").IsLocal);

        Write(("b.th", 4));
        await vm.RefreshCommand.ExecuteAsync(null);

        var demucs = Item(vm, "htdemucs_ft.yaml");
        Assert.True(demucs.IsLocal);
        Assert.Equal(12, demucs.FileSizeBytes);
    }

    [Fact]
    public async Task DeleteModel_ConfigSharedWithADownloadedModel_IsKept()
    {
        Write(("x.ckpt", 1), ("y.ckpt", 1), ("shared.yaml", 1));
        var vm = await LoadAsync();

        vm.DeleteModelCommand.Execute(Item(vm, "x.ckpt"));

        Assert.False(File.Exists(Path.Combine(ModelsDir, "x.ckpt")));
        Assert.True(File.Exists(Path.Combine(ModelsDir, "shared.yaml")));
        Assert.True(Item(vm, "y.ckpt").IsLocal);
    }

    [Fact]
    public async Task DeleteModel_Demucs_RemovesEveryFile()
    {
        Write(("a.th", 1), ("b.th", 1), ("htdemucs_ft.yaml", 1));
        var vm = await LoadAsync();

        vm.DeleteModelCommand.Execute(Item(vm, "htdemucs_ft.yaml"));

        Assert.Empty(Directory.GetFiles(ModelsDir));
        Assert.False(Item(vm, "htdemucs_ft.yaml").IsLocal);
    }

    [Fact]
    public async Task A_preset_saved_with_stems_unticked_persists_only_the_kept_stems()
    {
        var vm = await LoadAsync();
        Item(vm, "x.ckpt").IsChecked = true;

        Assert.True(vm.HasStemPicker);
        vm.StemChoices.Single(choice => choice.Name == "other").IsKept = false;
        vm.NewPresetName = "Vocals only";
        vm.SavePresetCommand.Execute(null);

        var saved = UserPresetService.Load(
            Path.Combine(_root, "user_presets.json"),
            Path.Combine(_root, "legacy.json")
        );
        var step = Assert.Single(Assert.Single(saved.Presets).Steps);
        Assert.Equal(["vocals"], step.KeepSet);
    }

    [Fact]
    public async Task A_preset_saved_with_every_stem_ticked_keeps_all()
    {
        var vm = await LoadAsync();
        Item(vm, "x.ckpt").IsChecked = true;
        vm.NewPresetName = "Everything";
        vm.SavePresetCommand.Execute(null);

        var saved = UserPresetService.Load(
            Path.Combine(_root, "user_presets.json"),
            Path.Combine(_root, "legacy.json")
        );
        Assert.Null(Assert.Single(Assert.Single(saved.Presets).Steps).KeepSet);
    }

    [Fact]
    public async Task Unticking_every_stem_blocks_saving()
    {
        var vm = await LoadAsync();
        Item(vm, "x.ckpt").IsChecked = true;
        vm.NewPresetName = "Nothing";
        foreach (var choice in vm.StemChoices)
            choice.IsKept = false;

        Assert.False(vm.SavePresetCommand.CanExecute(null));
    }

    private async Task<ModelsViewModel> LoadAsync()
    {
        var paths = new AppPaths(
            new AppSettings { ModelsDirectory = ModelsDir },
            PlatformInfo.Current
        );
        var runner = new FakeProcessRunner();
        runner.Setup(paths.SeparationDriverPython, Catalog);

        var vm = new ModelsViewModel(
            paths,
            new UserPresetService(Path.Combine(_root, "user_presets.json")),
            new ModelCatalogService(runner, paths),
            new ModelProfileResolver(),
            new ToolStateService(new SetupDetector(runner, paths))
        );
        await vm.RefreshCommand.ExecuteAsync(null);

        return vm;
    }

    private static ModelItemViewModel Item(ModelsViewModel vm, string filename) =>
        vm.Models.Single(item => item.Filename == filename);

    private void Write(params (string Name, int Bytes)[] files)
    {
        Directory.CreateDirectory(ModelsDir);
        foreach (var (name, bytes) in files)
            File.WriteAllBytes(Path.Combine(ModelsDir, name), new byte[bytes]);
    }
}

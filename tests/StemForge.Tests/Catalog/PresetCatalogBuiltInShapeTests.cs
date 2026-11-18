namespace StemForge.Tests.Catalog;

public sealed class PresetCatalogBuiltInShapeTests
{
    [Fact]
    public void BuiltIn_IdsAreUnique() =>
        Assert.Equal(
            PresetCatalog.BuiltIn.Count,
            PresetCatalog.BuiltIn.Select(p => p.Id).Distinct().Count()
        );

    [Fact]
    public void BuiltIn_EveryPresetListsAsManyModelsAsItsModelCount() =>
        Assert.All(
            PresetCatalog.BuiltIn,
            p =>
                Assert.True(
                    p.ModelCount == p.AllModels.Count,
                    $"'{p.Id}' says {p.ModelCount} models but lists {p.AllModels.Count}"
                )
        );

    [Fact]
    public void BuiltIn_EveryPresetHasALabelAndDescriptionToShow() =>
        Assert.All(
            PresetCatalog.BuiltIn,
            p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Label), p.Id);
                Assert.False(string.IsNullOrWhiteSpace(p.Description), p.Id);
            }
        );

    [Fact]
    public void BuiltIn_ModelListsHaveNoDuplicatesWithinAPreset() =>
        Assert.All(
            PresetCatalog.BuiltIn,
            p => Assert.Equal(p.AllModels.Count, p.AllModels.Distinct().Count())
        );

    [Fact]
    public void BuiltIn_AreAllBuiltinPresetMode() =>
        Assert.All(PresetCatalog.BuiltIn, p => Assert.Equal(SeparationMode.BuiltinPreset, p.Mode));
}

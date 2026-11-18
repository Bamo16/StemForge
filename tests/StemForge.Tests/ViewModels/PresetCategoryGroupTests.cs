using Avalonia.Media;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class PresetCategoryGroupTests
{
    private static PresetItemViewModel Item(string id, PresetCategory category) =>
        new(new Preset(id, id, category, "desc", ModelCount: 1, Vram: string.Empty));

    [Theory]
    [InlineData(PresetCategory.Vocals, "VOCALS")]
    [InlineData(PresetCategory.Instrumentals, "INSTRUMENTALS")]
    [InlineData(PresetCategory.Other, "OTHER")]
    [InlineData(PresetCategory.Drums, "DRUMS")]
    [InlineData(PresetCategory.Guitar, "GUITAR")]
    public void Label_IsTheUpperCasedCategory(PresetCategory category, string expected)
    {
        var group = new PresetCategoryGroup(category, Brushes.Red, []);

        Assert.Equal(expected, group.Label);
    }

    [Fact]
    public void Items_KeepTheOrderTheyWereGivenIn()
    {
        var group = new PresetCategoryGroup(
            PresetCategory.Vocals,
            Brushes.Red,
            [Item("b", PresetCategory.Vocals), Item("a", PresetCategory.Vocals)]
        );

        Assert.Equal(["b", "a"], group.Items.Select(item => item.Id));
    }

    [Fact]
    public void Items_AreACopyOfTheSource_SoLaterChangesToTheSourceDoNotShowUp()
    {
        List<PresetItemViewModel> source = [Item("a", PresetCategory.Vocals)];
        var group = new PresetCategoryGroup(PresetCategory.Vocals, Brushes.Red, source);

        source.Add(Item("b", PresetCategory.Vocals));

        Assert.Single(group.Items);
    }
}

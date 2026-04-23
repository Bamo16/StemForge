using Avalonia.Controls;
using Avalonia.Input;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class SeparateView : UserControl
{
    public SeparateView()
    {
        InitializeComponent();
    }

    private void OnPresetCardClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PresetItemViewModel item })
        {
            item.IsSelected = !item.IsSelected;
        }
    }
}

using System.ComponentModel;
using System.Windows.Input;

namespace StemForge.ViewModels;

/// <summary>
/// Read-only window onto the wizard's variant-selection state, passed to the row VM for tools
/// that have variants (audio-separator today). Lets the row's DataTemplate bind the picker UI
/// directly to a non-null path instead of jumping back to the wizard via ancestor bindings,
/// which is brittle in Avalonia compiled-binding mode.
/// </summary>
public interface IVariantPicker : INotifyPropertyChanged
{
    bool IsCpu { get; }
    bool IsCuda { get; }
    bool IsDirectML { get; }
    string GpuHint { get; }
    ICommand SetVariantCommand { get; }
}

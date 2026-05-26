using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using StemForge.Extensions;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class SeparateView : UserControl
{
    public SeparateView()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DropEvent, OnDropZoneDrop);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDropZoneDragOver);
        AddHandler(
            PointerPressedEvent,
            OnGlobalPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true
        );

        FormatPickerAnchor.PropertyChanged += OnAnchorOrOverlayChanged;
        FormatPickerOverlay.PropertyChanged += OnAnchorOrOverlayChanged;
    }

    private void OnAnchorOrOverlayChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty || e.Property == IsVisibleProperty)
            UpdateOverlayPosition();
    }

    private void UpdateOverlayPosition()
    {
        if (!FormatPickerOverlay.IsVisible || !FormatPickerAnchor.IsVisible)
            return;
        if (FormatPickerOverlay.GetVisualParent() is not Visual parent)
            return;

        var anchorBottomLeft = new Point(0, FormatPickerAnchor.Bounds.Height);
        if (FormatPickerAnchor.TranslatePoint(anchorBottomLeft, parent) is not { } pt)
            return;

        FormatPickerOverlay.Margin = new Thickness(pt.X, pt.Y + 4, 0, 0);
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SeparateViewModel vm || !vm.IsFormatPickerOpen)
            return;
        if (e.Source is not Visual source)
            return;

        if (IsWithin(source, FormatPickerOverlay) || IsWithin(source, FormatPickerAnchor))
            return;

        vm.IsFormatPickerOpen = false;
    }

    private static bool IsWithin(Visual target, Visual? ancestor)
    {
        if (ancestor is null)
            return false;
        Visual? cur = target;
        while (cur is not null)
        {
            if (cur == ancestor)
                return true;
            cur = cur.GetVisualParent();
        }
        return false;
    }

    private SeparateViewModel Vm => (SeparateViewModel)DataContext!;

    private async void OnDropZoneClicked(object? sender, PointerPressedEventArgs e)
    {
        var files = await this.PickFilesAsync([
            new FilePickerFileType("Audio Files")
            {
                Patterns = ["*.mp3", "*.flac", "*.wav", "*.m4a"],
            },
        ]);

        if (files.Count > 0)
            Vm.AddFilesToQueue(files);
    }

    private static void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files is { Count: > 0 })
            Vm.AddFilesToQueue(files.Select(f => f.Path.LocalPath));
    }

    private async void OnBrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (await this.PickFolderAsync(Vm.ExpandedOutputPath) is { } folder)
            Vm.OutputPath = folder;
    }

    private void OnPresetCardClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PresetItemViewModel item })
            item.IsSelected = !item.IsSelected;
    }

    private void OnFormatRowClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: FormatPickerItem item })
            Vm.SelectFormatCommand.Execute(item);
    }
}

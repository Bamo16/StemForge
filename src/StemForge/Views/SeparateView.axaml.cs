using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class SeparateView : UserControl
{
    public SeparateView()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DropEvent, OnDropZoneDrop);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDropZoneDragOver);
    }

    private SeparateViewModel Vm => (SeparateViewModel)DataContext!;

    private async void OnDropZoneClicked(object? sender, PointerPressedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Audio Files")
                    {
                        Patterns = ["*.mp3", "*.flac", "*.wav", "*.m4a"],
                    },
                ],
            }
        );

        if (files.Count > 0)
            Vm.AddFilesToQueue(files.Select(f => f.Path.LocalPath));
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
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        IStorageFolder? startLocation = null;
        try
        {
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                Vm.ExpandedOutputPath
            );
        }
        catch { }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
            }
        );

        if (folders.Count > 0)
            Vm.OutputPath = folders[0].Path.LocalPath;
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

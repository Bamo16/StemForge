using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public enum SeparateMode { Presets, Models }

public partial class SeparateViewModel : PageViewModelBase
{
    public override string Title => "Separate";

    public ObservableCollection<PresetCategoryGroup> Categories { get; }

    [ObservableProperty]
    private SeparateMode _mode = SeparateMode.Presets;

    public bool IsPresetsMode => Mode == SeparateMode.Presets;
    public bool IsModelsMode => Mode == SeparateMode.Models;

    [ObservableProperty]
    private string? _inputFilePath;

    [ObservableProperty]
    private string _urlInput = string.Empty;

    [ObservableProperty]
    private string _outputPath = "~/Music/Stems";

    [ObservableProperty]
    private int _selectedCount;

    public string SelectedCountLabel =>
        SelectedCount == 0
            ? "No presets selected"
            : $" preset{(SelectedCount == 1 ? "" : "s")} selected";

    public bool HasSelection => SelectedCount > 0;

    public bool CanRun => SelectedCount > 0 && (!string.IsNullOrWhiteSpace(InputFilePath) || !string.IsNullOrWhiteSpace(UrlInput));

    public SeparateViewModel()
    {
        Categories = new ObservableCollection<PresetCategoryGroup>(BuildGroups());

        foreach (var g in Categories)
        {
            foreach (var item in g.Items)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }
    }

    private static IEnumerable<PresetCategoryGroup> BuildGroups()
    {
        // Pull brushes from the app-level resources by key.
        var app = Application.Current!;
        IBrush Brush(string key)
        {
            if (app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush b)
            {
                return b;
            }
            return Brushes.Transparent;
        }

        var byCategory = PresetCatalog.BuiltIn
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Select(p => new PresetItemViewModel(p)));

        foreach (var category in new[] { PresetCategory.Vocals, PresetCategory.Instrumentals, PresetCategory.Other })
        {
            if (!byCategory.TryGetValue(category, out var items)) continue;
            var brush = category switch
            {
                PresetCategory.Vocals => Brush("CategoryVocalsBrush"),
                PresetCategory.Instrumentals => Brush("CategoryInstrumentalsBrush"),
                PresetCategory.Other => Brush("CategoryOtherBrush"),
                _ => Brush("TextSecondaryBrush"),
            };
            yield return new PresetCategoryGroup(category, brush, items);
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresetItemViewModel.IsSelected))
        {
            RecomputeSelectedCount();
        }
    }

    private void RecomputeSelectedCount()
    {
        SelectedCount = Categories.Sum(g => g.Items.Count(i => i.IsSelected));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedCountLabel));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnUrlInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnModeChanged(SeparateMode value)
    {
        OnPropertyChanged(nameof(IsPresetsMode));
        OnPropertyChanged(nameof(IsModelsMode));
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        Mode = mode.Equals("Models", StringComparison.OrdinalIgnoreCase) ? SeparateMode.Models : SeparateMode.Presets;
    }

    [RelayCommand]
    private void TogglePreset(PresetItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
    }

    [RelayCommand]
    private void Browse()
    {
        // TODO: wire up StorageProvider.OpenFilePickerAsync
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        // TODO: wire up StorageProvider.OpenFolderPickerAsync
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run()
    {
        // TODO: enqueue job and run
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddToQueue()
    {
        // TODO: enqueue selected presets
    }
}

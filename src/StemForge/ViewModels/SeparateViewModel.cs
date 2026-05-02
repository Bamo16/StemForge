using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Extensions;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public enum SeparateMode
{
    BuiltIn,
    MyPresets,
}

public partial class SeparateViewModel : PageViewModelBase
{
    public override string Title => "Separate";

    public ObservableCollection<PresetCategoryGroup> Categories { get; }

    [ObservableProperty]
    public partial SeparateMode Mode { get; set; } = SeparateMode.BuiltIn;

    public bool IsBuiltInMode => Mode == SeparateMode.BuiltIn;
    public bool IsMyPresetsMode => Mode == SeparateMode.MyPresets;

    [ObservableProperty]
    public partial string? InputFilePath { get; set; }

    public bool HasInputFile => !string.IsNullOrWhiteSpace(InputFilePath);
    public string InputFileName =>
        string.IsNullOrWhiteSpace(InputFilePath) ? string.Empty : Path.GetFileName(InputFilePath);

    [ObservableProperty]
    public partial string UrlInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputPath { get; set; } = "~/Music/Stems";

    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    public string SelectedCountLabel =>
        SelectedCount == 0
            ? "No presets selected"
            : $" preset{(SelectedCount == 1 ? "" : "s")} selected";

    public bool HasSelection => SelectedCount > 0;

    private readonly JobQueueService _queue;
    private readonly AppSettingsService _settings;
    private readonly UserPresetService _userPresets;

    public event Action? NavigateToQueueRequested;

    public bool CanStartRun =>
        SelectedCount > 0
        && (!string.IsNullOrWhiteSpace(InputFilePath) || !string.IsNullOrWhiteSpace(UrlInput));

    // ── User presets ──────────────────────────────────────────────────────────

    public ObservableCollection<PresetItemViewModel> UserPresetItems { get; } = [];
    public bool HasUserPresets => UserPresetItems.Count > 0;

    public SeparateViewModel(
        JobQueueService queue,
        AppSettingsService settings,
        UserPresetService userPresets
    )
    {
        _queue = queue;
        _settings = settings;
        _userPresets = userPresets;
        OutputPath = settings.Current.OutputDirectory;
        Categories = new ObservableCollection<PresetCategoryGroup>(BuildGroups());

        foreach (var g in Categories)
        foreach (var item in g.Items)
            item.PropertyChanged += OnItemPropertyChanged;

        foreach (var p in _userPresets.Presets)
            AddUserPresetItem(p);

        _userPresets.Presets.CollectionChanged += OnUserPresetsCollectionChanged;
    }

    private void AddUserPresetItem(Preset p)
    {
        var vm = new PresetItemViewModel(p);
        vm.PropertyChanged += OnItemPropertyChanged;
        UserPresetItems.Add(vm);
    }

    private void OnUserPresetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (Preset p in e.NewItems!)
                    AddUserPresetItem(p);
                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (Preset p in e.OldItems!)
                {
                    var vm = UserPresetItems.FirstOrDefault(i => i.Id == p.Id);
                    if (vm is not null)
                    {
                        vm.PropertyChanged -= OnItemPropertyChanged;
                        UserPresetItems.Remove(vm);
                    }
                }
                break;
            default:
                foreach (var vm in UserPresetItems)
                    vm.PropertyChanged -= OnItemPropertyChanged;
                UserPresetItems.Clear();
                foreach (var p in _userPresets.Presets)
                    AddUserPresetItem(p);
                break;
        }
        OnPropertyChanged(nameof(HasUserPresets));
        RecomputeSelectedCount();
    }

    [RelayCommand]
    private void RemoveUserPreset(PresetItemViewModel item)
    {
        _userPresets.Remove(item.Id);
    }

    // ── Preset building ───────────────────────────────────────────────────────

    private static IEnumerable<PresetCategoryGroup> BuildGroups()
    {
        var app = Application.Current!;
        IBrush Brush(string key)
        {
            if (
                app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value)
                && value is IBrush b
            )
                return b;
            return Brushes.Transparent;
        }

        var byCategory = PresetCatalog
            .BuiltIn.GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Select(p => new PresetItemViewModel(p)));

        foreach (
            var category in new[]
            {
                PresetCategory.Vocals,
                PresetCategory.Instrumentals,
                PresetCategory.Other,
            }
        )
        {
            if (!byCategory.TryGetValue(category, out var items))
                continue;
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

    // ── Property notifications ────────────────────────────────────────────────

    private void OnItemPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(PresetItemViewModel.IsSelected))
            RecomputeSelectedCount();
    }

    private void RecomputeSelectedCount()
    {
        SelectedCount =
            Categories.Sum(g => g.Items.Count(i => i.IsSelected))
            + UserPresetItems.Count(i => i.IsSelected);
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedCountLabel));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanStartRun));
        RunCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(CanStartRun));
        OnPropertyChanged(nameof(HasInputFile));
        OnPropertyChanged(nameof(InputFileName));
        RunCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearInput()
    {
        InputFilePath = null;
    }

    partial void OnUrlInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanStartRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnModeChanged(SeparateMode value)
    {
        OnPropertyChanged(nameof(IsBuiltInMode));
        OnPropertyChanged(nameof(IsMyPresetsMode));
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        Mode = mode.Equals("MyPresets", StringComparison.OrdinalIgnoreCase)
            ? SeparateMode.MyPresets
            : SeparateMode.BuiltIn;
    }

    [RelayCommand]
    private void TogglePreset(PresetItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private void Run()
    {
        AddToQueue();
        NavigateToQueueRequested?.Invoke();
    }

    private static string ExpandPath(string path) =>
        path.StartsWith('~')
            ? Environment.SpecialFolder.UserProfile.GetFolderPath(path.TrimStart('~', '/', '\\'))
            : path;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddToQueue()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) && string.IsNullOrWhiteSpace(UrlInput))
            return;

        var selectedPresets = Categories
            .SelectMany(g => g.Items)
            .Concat(UserPresetItems)
            .Where(i => i.IsSelected)
            .Select(i => i.Preset)
            .ToList();

        var hasUrl = !string.IsNullOrWhiteSpace(UrlInput);
        var record = new JobRecord(
            Guid.NewGuid(),
            hasUrl ? null : InputFilePath,
            hasUrl ? UrlInput : null,
            selectedPresets,
            ExpandPath(OutputPath),
            _settings.Current.ModelsDirectory
        );

        _queue.Enqueue(record);
    }
}

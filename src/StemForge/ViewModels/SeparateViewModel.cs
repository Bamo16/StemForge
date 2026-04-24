using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Models;
using StemForge.Services;

namespace StemForge.ViewModels;

public enum SeparateMode
{
    Presets,
    Models,
}

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

    public bool HasInputFile => !string.IsNullOrWhiteSpace(InputFilePath);
    public string InputFileName =>
        string.IsNullOrWhiteSpace(InputFilePath) ? string.Empty : Path.GetFileName(InputFilePath);

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    private CancellationTokenSource? _runCts;
    private readonly JobQueueService _queue;

    public bool CanStartRun =>
        !IsRunning
        && SelectedCount > 0
        && (!string.IsNullOrWhiteSpace(InputFilePath) || !string.IsNullOrWhiteSpace(UrlInput));

    public SeparateViewModel(JobQueueService queue)
    {
        _queue = queue;
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
            if (
                app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value)
                && value is IBrush b
            )
            {
                return b;
            }
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

    private void OnItemPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
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
        OnPropertyChanged(nameof(IsPresetsMode));
        OnPropertyChanged(nameof(IsModelsMode));
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        Mode = mode.Equals("Models", StringComparison.OrdinalIgnoreCase)
            ? SeparateMode.Models
            : SeparateMode.Presets;
    }

    [RelayCommand]
    private void TogglePreset(PresetItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        IsRunning = true;
        ErrorMessage = null;
        Progress = 0;

        try
        {
            var selectedPresets = Categories
                .SelectMany(g => g.Items)
                .Where(i => i.IsSelected)
                .Select(i => i.Preset)
                .ToList();

            var modelsDir = SeparationService.ResolveModelsDir();
            var outputDir = ExpandPath(OutputPath);
            var service = new SeparationService();

            var progressReporter = new Progress<SeparationProgress>(p =>
            {
                Progress = p.OverallPercent;
                StatusText = p.StatusText;
            });

            await service.RunAsync(
                InputFilePath!,
                selectedPresets,
                outputDir,
                modelsDir,
                progressReporter,
                cts.Token
            );

            StatusText = $"Done — {selectedPresets.Count} stem{(selectedPresets.Count == 1 ? "" : "s")} written";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            Progress = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = "Failed";
        }
        finally
        {
            IsRunning = false;
            _runCts = null;
        }
    }

    [RelayCommand]
    private void CancelRun() => _runCts?.Cancel();

    private static string ExpandPath(string path) =>
        path.StartsWith("~/")
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]
            )
            : path;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddToQueue()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) && string.IsNullOrWhiteSpace(UrlInput))
            return;

        var selectedPresets = Categories
            .SelectMany(g => g.Items)
            .Where(i => i.IsSelected)
            .Select(i => i.Preset)
            .ToList();

        var record = new JobRecord(
            Guid.NewGuid(),
            InputFilePath ?? UrlInput,
            selectedPresets,
            ExpandPath(OutputPath)
        );

        _queue.Enqueue(record);
    }
}

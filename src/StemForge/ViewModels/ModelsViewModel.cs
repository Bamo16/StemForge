using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.ViewModels;

public sealed record EnsembleOption(string Key, string Tip)
{
    public override string ToString() => Key;
}

public partial class ModelsViewModel : PageViewModelBase
{
    public override string Title => "Model Library";

    private readonly ModelCatalogService _catalog;
    private readonly AppPaths _paths;
    private readonly UserPresetService _userPresets;
    private readonly ToolStateService _toolState;
    private readonly List<ModelItemViewModel> _all = [];
    private bool _wasAudioSeparatorAvailable;

    public ModelsViewModel(
        AppPaths paths,
        UserPresetService userPresets,
        ModelCatalogService catalog,
        ToolStateService toolState
    )
    {
        _catalog = catalog;
        _paths = paths;
        _userPresets = userPresets;
        _toolState = toolState;
        EnsembleAlgorithm = EnsembleAlgorithmOptions[0];
        _wasAudioSeparatorAvailable = _toolState.IsAudioSeparatorAvailable;
        _toolState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ToolStateService.IsAudioSeparatorAvailable))
                return;
            var nowAvailable = _toolState.IsAudioSeparatorAvailable;
            if (nowAvailable && !_wasAudioSeparatorAvailable)
                _ = LoadModelsAsync(forceRefresh: true);
            _wasAudioSeparatorAvailable = nowAvailable;
        };
        _ = LoadModelsAsync();
    }

    // ── Loading state ─────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;
    public bool HasModels => !IsLoading && ErrorText is null && Models.Count > 0;
    public bool IsEmpty => !IsLoading && ErrorText is null && Models.Count == 0;

    partial void OnIsLoadingChanged(bool value) => NotifyListState();

    partial void OnErrorTextChanged(string? value) => NotifyListState();

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasModels));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ── Model list ────────────────────────────────────────────────────────────

    public ObservableCollection<ModelItemViewModel> Models { get; } = [];

    // ── Filters ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StemFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowLocalOnly { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnStemFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterVocals));
        OnPropertyChanged(nameof(IsFilterInstrumental));
        OnPropertyChanged(nameof(IsFilterDrums));
        OnPropertyChanged(nameof(IsFilterBass));
        OnPropertyChanged(nameof(IsFilterOther));
        ApplyFilter();
    }

    partial void OnShowLocalOnlyChanged(bool value) => ApplyFilter();

    public bool IsFilterAll => StemFilter == string.Empty;
    public bool IsFilterVocals => StemFilter == "vocals";
    public bool IsFilterInstrumental => StemFilter == "instrumental";
    public bool IsFilterDrums => StemFilter == "drums";
    public bool IsFilterBass => StemFilter == "bass";
    public bool IsFilterOther => StemFilter == "other";

    [RelayCommand]
    private void SetStemFilter(string stem) => StemFilter = stem;

    [RelayCommand]
    private void ToggleLocalOnly() => ShowLocalOnly = !ShowLocalOnly;

    // ── Multi-select ──────────────────────────────────────────────────────────

    public int CheckedCount => _all.Count(m => m.IsChecked);
    public bool HasChecked => _all.Any(m => m.IsChecked);
    public bool IsMultiModel => CheckedCount > 1;

    public string CheckedSummary =>
        CheckedCount == 0
            ? string.Empty
            : string.Join(", ", _all.Where(m => m.IsChecked).Select(m => m.FriendlyName));

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModelItemViewModel.IsChecked))
            return;
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(IsMultiModel));
        OnPropertyChanged(nameof(CheckedSummary));
        SavePresetCommand.NotifyCanExecuteChanged();
    }

    // ── Save as preset ────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string NewPresetName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial EnsembleOption EnsembleAlgorithm { get; set; } = null!;

    partial void OnNewPresetNameChanged(string value) =>
        SavePresetCommand.NotifyCanExecuteChanged();

    public IReadOnlyList<EnsembleOption> EnsembleAlgorithmOptions { get; } =
        EnsembleAlgorithmCatalog
            .Known.Select(a => new EnsembleOption(a.Key, a.Description))
            .ToList();

    private bool CanSavePreset => HasChecked && !string.IsNullOrWhiteSpace(NewPresetName);

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        var checked_ = _all.Where(m => m.IsChecked).ToList();
        if (checked_.Count == 0)
            return;

        var id = SanitizeId(NewPresetName);
        var description =
            checked_.Count == 1
                ? checked_[0].FriendlyName
                : string.Join(" + ", checked_.Select(m => m.FriendlyName));

        Preset preset;
        if (checked_.Count == 1)
        {
            preset = new Preset(
                Id: id,
                Label: NewPresetName.Trim(),
                Category: PresetCategory.Other,
                Description: description,
                ModelCount: 1,
                Vram: string.Empty,
                Mode: SeparationMode.SingleModel,
                PrimaryModel: checked_[0].Filename
            );
        }
        else
        {
            preset = new Preset(
                Id: id,
                Label: NewPresetName.Trim(),
                Category: PresetCategory.Other,
                Description: description,
                ModelCount: checked_.Count,
                Vram: string.Empty,
                Mode: SeparationMode.CustomEnsemble,
                PrimaryModel: checked_[0].Filename,
                EnsembleAlgorithm: EnsembleAlgorithm.Key,
                ExtraModels: checked_.Skip(1).Select(m => m.Filename).ToList()
            );
        }

        _userPresets.Add(preset);
        AppLogger.Info(nameof(ModelsViewModel), $"Saved preset: {preset.Label}");

        foreach (var m in _all)
            m.IsChecked = false;
        NewPresetName = string.Empty;
    }

    // ── Delete local model file ───────────────────────────────────────────────

    [RelayCommand]
    private void DeleteModel(ModelItemViewModel item)
    {
        var path = Path.Combine(_paths.ModelsDirectory, item.Filename);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            item.IsLocal = false;
            item.FileSizeBytes = 0;
            AppLogger.Info(nameof(ModelsViewModel), $"Deleted local model: {item.Filename}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                nameof(ModelsViewModel),
                $"Delete failed for {item.Filename}: {ex.Message}"
            );
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh()
    {
        _catalog.Invalidate();
        await LoadModelsAsync(forceRefresh: true);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task LoadModelsAsync(bool forceRefresh = false)
    {
        IsLoading = true;
        ErrorText = null;

        foreach (var m in _all)
            m.PropertyChanged -= OnItemPropertyChanged;
        _all.Clear();
        Models.Clear();

        try
        {
            var models = await _catalog.ListModelsAsync(_paths.AudioSeparator, forceRefresh);
            var modelsDir = _paths.ModelsDirectory;

            foreach (var m in models)
            {
                var fullPath = Path.Combine(modelsDir, m.Filename);
                var exists = File.Exists(fullPath);
                var vm = new ModelItemViewModel(m)
                {
                    IsLocal = exists,
                    FileSizeBytes = exists ? new FileInfo(fullPath).Length : 0,
                };
                vm.PropertyChanged += OnItemPropertyChanged;
                _all.Add(vm);
            }

            ApplyFilter();

            if (_all.Count == 0)
                ErrorText = "No models found. Make sure audio-separator is installed.";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var stem = StemFilter;

        var filtered = _all.Where(m =>
        {
            if (ShowLocalOnly && !m.IsLocal)
                return false;

            if (!string.IsNullOrEmpty(search))
            {
                if (
                    !m.FriendlyName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !m.Filename.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !m.Architecture.Contains(search, StringComparison.OrdinalIgnoreCase)
                )
                    return false;
            }

            if (!string.IsNullOrEmpty(stem))
            {
                if (!m.Stems.Any(s => s.Name.Equals(stem, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        });

        Models.Clear();
        foreach (var m in filtered)
            Models.Add(m);

        NotifyListState();
    }

    private static string SanitizeId(string name) =>
        new string(
            name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray()
        );
}

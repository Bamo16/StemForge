using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly AppSettings _settings;
    private readonly UserPresetService _userPresets;
    private readonly ToolStateService _toolState;
    private readonly AppPaths _paths;
    private readonly YouTubeAudioService _ytAudio;
    private CancellationTokenSource? _urlCheckCts;

    public event Action? NavigateToQueueRequested;

    [ObservableProperty]
    public partial bool IsUrlInputEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlTitle))]
    [NotifyPropertyChangedFor(nameof(ShowUrlFormatRow))]
    public partial string? UrlTitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlCodec))]
    public partial string? UrlCodec { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlBitrate))]
    public partial string? UrlBitrate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlDuration))]
    public partial string? UrlDuration { get; set; }

    public bool HasUrlTitle => UrlTitle is not null;
    public bool HasUrlCodec => UrlCodec is not null;
    public bool HasUrlBitrate => UrlBitrate is not null;
    public bool HasUrlDuration => UrlDuration is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUrlFormatRow))]
    public partial bool IsCheckingUrl { get; set; }

    [ObservableProperty]
    public partial string LoadingDots { get; set; } = "";

    public bool ShowUrlFormatRow => IsUrlInputEnabled && (IsCheckingUrl || UrlTitle is not null);

    [ObservableProperty]
    public partial AudioFormat StemOutputFormat { get; set; } = AudioFormat.Flac;

    [ObservableProperty]
    public partial bool KeepSourceFile { get; set; }

    public IReadOnlyList<AudioFormat> AudioFormatOptions { get; } =
        [AudioFormat.Flac, AudioFormat.Wav, AudioFormat.Mp3];

    public bool CanStartRun =>
        SelectedCount > 0
        && (
            !string.IsNullOrWhiteSpace(InputFilePath)
            || (IsUrlInputEnabled && !string.IsNullOrWhiteSpace(UrlInput))
        );

    // ── User presets ──────────────────────────────────────────────────────────

    public ObservableCollection<PresetItemViewModel> UserPresetItems { get; } = [];
    public bool HasUserPresets => UserPresetItems.Count > 0;

    public SeparateViewModel(
        JobQueueService queue,
        AppSettings settings,
        UserPresetService userPresets,
        ToolStateService toolState,
        AppPaths paths,
        YouTubeAudioService ytAudio
    )
    {
        _queue = queue;
        _settings = settings;
        _ytAudio = ytAudio;
        _userPresets = userPresets;
        _toolState = toolState;
        _paths = paths;
        OutputPath = paths.OutputDirectory;
        StemOutputFormat = settings.DefaultAudioFormat;
        IsUrlInputEnabled = _toolState.CanDownloadFromUrl;
        _toolState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolStateService.CanDownloadFromUrl))
                IsUrlInputEnabled = _toolState.CanDownloadFromUrl;
        };
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

        _urlCheckCts?.Cancel();
        _urlCheckCts = null;
        ClearUrlMetadata();
        IsCheckingUrl = false;

        if (!IsUrlInputEnabled || string.IsNullOrWhiteSpace(value))
            return;

        var cts = new CancellationTokenSource();
        _urlCheckCts = cts;
        _ = FetchUrlFormatAsync(value, cts.Token);
    }

    private static readonly string[] _dotFrames = [".", "..", "..."];
    private int _dotIndex;
    private DispatcherTimer? _dotTimer;

    partial void OnIsCheckingUrlChanged(bool value)
    {
        if (value)
        {
            _dotIndex = 0;
            LoadingDots = _dotFrames[0];
            _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
            _dotTimer.Tick += (_, _) =>
            {
                _dotIndex = (_dotIndex + 1) % _dotFrames.Length;
                LoadingDots = _dotFrames[_dotIndex];
            };
            _dotTimer.Start();
        }
        else
        {
            _dotTimer?.Stop();
            _dotTimer = null;
            LoadingDots = "";
        }
    }

    private void ClearUrlMetadata()
    {
        UrlTitle = null;
        UrlCodec = null;
        UrlBitrate = null;
        UrlDuration = null;
    }

    private async Task FetchUrlFormatAsync(string url, CancellationToken ct)
    {
        IsCheckingUrl = true;
        try
        {
            await Task.Delay(800, ct);
            var meta = await _ytAudio.GetAudioFormatInfoAsync(url, _settings, ct);
            if (meta is null)
                return;

            UrlTitle = meta.Title;
            if (meta.SourceCodec is { Length: > 0 } codec && codec != "none")
                UrlCodec = codec;
            if (meta.SourceBitrateKbps is { } kbps)
                UrlBitrate = $"{kbps:F0} kb/s";
            if (meta.DurationSeconds is { } dur)
            {
                var ts = TimeSpan.FromSeconds(dur);
                UrlDuration = ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsCheckingUrl = false;
        }
    }

    partial void OnIsUrlInputEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartRun));
        OnPropertyChanged(nameof(ShowUrlFormatRow));
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
            _paths.ModelsDirectory,
            StemOutputFormat,
            KeepSourceFile && hasUrl
        );

        _queue.Enqueue(record);
    }
}

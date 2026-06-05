using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StemForge.Extensions;
using StemForge.Helpers;
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
    private readonly ISeparatorDriverService _driver;
    private CancellationTokenSource? _urlCheckCts;
    private YtDlpMetadata? _cachedUrlMeta;

    public event Action? NavigateToQueueRequested;
    public event Action? ShowWizardRequested;

    [ObservableProperty]
    public partial bool IsUrlInputEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLocalInputEnabled { get; set; }

    /// <summary>
    /// Tracks whether the user has gone through the setup wizard (successfully or by
    /// dismissal). While false, the blocked-input red overlays on the drop zone and URL
    /// field are suppressed — the user is already in the wizard flow, no need to nag
    /// them about missing tools they're about to install.
    /// </summary>
    [ObservableProperty]
    public partial bool HasCompletedSetup { get; set; }

    public bool ShowLocalBlockedMessage => HasCompletedSetup && !IsLocalInputEnabled;
    public bool ShowUrlBlockedMessage => HasCompletedSetup && !IsUrlInputEnabled;

    partial void OnHasCompletedSetupChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLocalBlockedMessage));
        OnPropertyChanged(nameof(ShowUrlBlockedMessage));
    }

    partial void OnIsLocalInputEnabledChanged(bool value) =>
        OnPropertyChanged(nameof(ShowLocalBlockedMessage));

    public string LocalInputBlockedMessage =>
        BuildBlockedMessage(LocalRequiredTools, "local files");

    public string UrlInputBlockedMessage => BuildBlockedMessage(UrlRequiredTools, "URL downloads");

    private IReadOnlyList<string> LocalRequiredTools
    {
        get
        {
            var missing = new List<string>(2);
            if (!_toolState.IsAudioSeparatorAvailable)
                missing.Add("audio-separator");
            if (!_toolState.IsFfmpegAvailable)
                missing.Add("ffmpeg");
            return missing;
        }
    }

    private IReadOnlyList<string> UrlRequiredTools
    {
        get
        {
            var missing = new List<string>(3);
            if (!_toolState.IsAudioSeparatorAvailable)
                missing.Add("audio-separator");
            if (!_toolState.IsFfmpegAvailable)
                missing.Add("ffmpeg");
            if (!_toolState.IsYtdlpAvailable)
                missing.Add("yt-dlp");
            return missing;
        }
    }

    private static string BuildBlockedMessage(IReadOnlyList<string> missing, string featureLabel)
    {
        if (missing.Count == 0)
            return string.Empty;
        var list = missing.Count switch
        {
            1 => missing[0],
            2 => $"{missing[0]} and {missing[1]}",
            _ => $"{string.Join(", ", missing.Take(missing.Count - 1))}, and {missing[^1]}",
        };
        return $"Install {list} to enable {featureLabel}.";
    }

    [RelayCommand]
    private void ShowWizard() => ShowWizardRequested?.Invoke();

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlSampleRate))]
    public partial string? UrlSampleRate { get; set; }

    public bool HasUrlTitle => UrlTitle is not null;
    public bool HasUrlCodec => UrlCodec is not null;
    public bool HasUrlBitrate => UrlBitrate is not null;
    public bool HasUrlDuration => UrlDuration is not null;
    public bool HasUrlSampleRate => UrlSampleRate is not null;

    // ── Local-file resolved metadata ──────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalCodec))]
    [NotifyPropertyChangedFor(nameof(ShowLocalChips))]
    public partial string? LocalCodec { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalBitrate))]
    [NotifyPropertyChangedFor(nameof(ShowLocalChips))]
    public partial string? LocalBitrate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalSampleRate))]
    [NotifyPropertyChangedFor(nameof(ShowLocalChips))]
    public partial string? LocalSampleRate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalDuration))]
    [NotifyPropertyChangedFor(nameof(ShowLocalChips))]
    public partial string? LocalDuration { get; set; }

    public bool HasLocalCodec => LocalCodec is not null;
    public bool HasLocalBitrate => LocalBitrate is not null;
    public bool HasLocalSampleRate => LocalSampleRate is not null;
    public bool HasLocalDuration => LocalDuration is not null;

    public bool ShowLocalChips =>
        HasInputFile
        && (HasLocalCodec || HasLocalBitrate || HasLocalSampleRate || HasLocalDuration);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUrlFormatRow))]
    public partial bool IsCheckingUrl { get; set; }

    [ObservableProperty]
    public partial double SpinnerAngle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlFetchError))]
    [NotifyPropertyChangedFor(nameof(ShowUrlFormatRow))]
    public partial string? UrlFetchError { get; set; }

    public bool HasUrlFetchError => UrlFetchError is not null;

    public bool ShowUrlFormatRow =>
        IsUrlInputEnabled && (IsCheckingUrl || UrlTitle is not null || HasUrlFetchError);

    [ObservableProperty]
    public partial AudioFormat StemOutputFormat { get; set; } = AudioFormat.Flac;

    [ObservableProperty]
    public partial bool KeepSourceFile { get; set; }

    [ObservableProperty]
    public partial bool ExtractDrums { get; set; }

    public IReadOnlyList<AudioFormat> AudioFormatOptions { get; } =
    [AudioFormat.Flac, AudioFormat.Wav, AudioFormat.Mp3];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlInputError))]
    public partial string? UrlInputError { get; set; }

    public bool HasUrlInputError => UrlInputError is not null;

    // ── Format picker ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFormatPicker))]
    [NotifyPropertyChangedFor(nameof(FormatPickerToggleLabel))]
    public partial bool IsFormatPickerOpen { get; set; }

    public bool HasFormatPicker => FormatPickerItems.Count > 1;
    public bool ShowFormatPicker => IsFormatPickerOpen && HasFormatPicker;
    public string FormatPickerToggleLabel =>
        $"{(IsFormatPickerOpen ? "▴" : "▾")} Format options ({FormatPickerItems.Count} available)";

    public ObservableCollection<FormatPickerItem> FormatPickerItems { get; } = [];

    private YtDlpFormat? _selectedFormatOverride;

    [RelayCommand]
    private void ToggleFormatPicker() => IsFormatPickerOpen = !IsFormatPickerOpen;

    [RelayCommand]
    private void SelectFormat(FormatPickerItem item)
    {
        _selectedFormatOverride = item.IsAutoRecommended ? null : item.Format;
        foreach (var f in FormatPickerItems)
            f.IsSelected = f == item;

        // Update chips to reflect the active format
        if (_selectedFormatOverride is { } ov)
        {
            if (ov.AudioCodec is { Length: > 0 } codec && codec != "none")
                UrlCodec = AudioFormatInfo.PrettyCodec(codec);
            var kbps = ov.AverageAudioBitrate ?? ov.AverageTotalBitrate;
            if (kbps.HasValue)
                UrlBitrate = $"{kbps.Value:F0} kb/s";
            UrlSampleRate = ov.AudioSampleRate is { } hz ? $"{hz / 1000.0:F1} kHz" : null;
        }
        else if (_cachedUrlMeta is { } auto)
        {
            UrlCodec =
                auto.SourceCodec is { Length: > 0 } c && c != "none"
                    ? AudioFormatInfo.PrettyCodec(c)
                    : null;
            UrlBitrate = auto.SourceBitrateKbps is { } k ? $"{k:F0} kb/s" : null;
            UrlSampleRate = auto.AudioFormats?.FirstOrDefault(f => f.FormatId == auto.FormatId)
                is { AudioSampleRate: { } autoHz }
                ? $"{autoHz / 1000.0:F1} kHz"
                : null;
        }

        IsFormatPickerOpen = false;
    }

    public bool CanStartRun =>
        SelectedCount > 0
        && (
            !string.IsNullOrWhiteSpace(InputFilePath)
            || (IsUrlInputEnabled && _cachedUrlMeta is not null)
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
        YouTubeAudioService ytAudio,
        ISeparatorDriverService driver
    )
    {
        _queue = queue;
        _settings = settings;
        _ytAudio = ytAudio;
        _userPresets = userPresets;
        _toolState = toolState;
        _paths = paths;
        _driver = driver;
        OutputPath = paths.OutputDirectory;
        StemOutputFormat = settings.DefaultAudioFormat;
        IsUrlInputEnabled = _toolState.CanDownloadFromUrl;
        IsLocalInputEnabled = _toolState.IsAudioSeparatorAvailable && _toolState.IsFfmpegAvailable;
        HasCompletedSetup = _settings.FirstRunComplete;
        _toolState.PropertyChanged += (_, _) =>
        {
            IsUrlInputEnabled = _toolState.CanDownloadFromUrl;
            IsLocalInputEnabled =
                _toolState.IsAudioSeparatorAvailable && _toolState.IsFfmpegAvailable;
            // Computed message properties depend on the same backing state — re-raise so
            // bindings re-evaluate the text and visibility flags.
            OnPropertyChanged(nameof(LocalInputBlockedMessage));
            OnPropertyChanged(nameof(UrlInputBlockedMessage));
        };
        Categories = new ObservableCollection<PresetCategoryGroup>(
            BuildGroups(PresetCatalog.BuiltIn)
        );
        _driver.PresetsLoaded += OnDriverPresetsLoaded;

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

    private void OnDriverPresetsLoaded(IReadOnlyList<Preset> presets)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshBuiltInCategories(presets));
    }

    private void RefreshBuiltInCategories(IReadOnlyList<Preset> presets)
    {
        // Preserve selection for any presets that are still present after the refresh.
        var selectedIds = Categories
            .SelectMany(g => g.Items)
            .Where(i => i.IsSelected)
            .Select(i => i.Id)
            .ToHashSet();

        foreach (var g in Categories)
        foreach (var item in g.Items)
            item.PropertyChanged -= OnItemPropertyChanged;

        Categories.Clear();
        foreach (var group in BuildGroups(presets))
        {
            foreach (var item in group.Items)
            {
                if (selectedIds.Contains(item.Id))
                    item.IsSelected = true;
                item.PropertyChanged += OnItemPropertyChanged;
            }
            Categories.Add(group);
        }

        RecomputeSelectedCount();
    }

    private static IEnumerable<PresetCategoryGroup> BuildGroups(IReadOnlyList<Preset> presets)
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

        var byCategory = presets
            .GroupBy(p => p.Category)
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
        NotifyCanRunChanged();
    }

    partial void OnInputFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasInputFile));
        OnPropertyChanged(nameof(InputFileName));
        NotifyCanRunChanged();

        if (value is not null)
        {
            var (codec, bitrate, sampleRate, duration) = AudioTagger.ReadAudioProperties(value);
            LocalCodec = codec;
            LocalBitrate = bitrate;
            LocalSampleRate = sampleRate;
            LocalDuration = duration;
        }
        else
        {
            LocalCodec = null;
            LocalBitrate = null;
            LocalSampleRate = null;
            LocalDuration = null;
        }

        OnPropertyChanged(nameof(ShowLocalChips));
    }

    [RelayCommand]
    private void ClearInput()
    {
        InputFilePath = null;
    }

    partial void OnUrlInputChanged(string value)
    {
        NotifyCanRunChanged();

        _urlCheckCts?.Cancel();
        _urlCheckCts = null;
        ClearUrlMetadata();
        IsCheckingUrl = false;

        // Clear the error whenever the field changes to empty or a valid URL.
        if (string.IsNullOrWhiteSpace(value) || YtUrlHelper.TryNormalize(value, out _))
            UrlInputError = null;

        if (!IsUrlInputEnabled || string.IsNullOrWhiteSpace(value))
            return;

        // Only spawn yt-dlp for recognisable URLs/video IDs.
        if (!YtUrlHelper.TryNormalize(value, out var normalized))
            return;

        var cts = new CancellationTokenSource();
        _urlCheckCts = cts;
        _ = FetchUrlFormatAsync(normalized, cts);
    }

    private DispatcherTimer? _spinnerTimer;

    partial void OnIsCheckingUrlChanged(bool value)
    {
        if (value)
        {
            SpinnerAngle = 0;
            _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _spinnerTimer.Tick += (_, _) => SpinnerAngle = (SpinnerAngle + 7.68) % 360;
            _spinnerTimer.Start();
        }
        else
        {
            _spinnerTimer?.Stop();
            _spinnerTimer = null;
        }
    }

    [RelayCommand]
    private void RetryFetch()
    {
        if (!IsUrlInputEnabled || !YtUrlHelper.TryNormalize(UrlInput, out var normalized))
            return;
        _urlCheckCts?.Cancel();
        var cts = new CancellationTokenSource();
        _urlCheckCts = cts;
        ClearUrlMetadata();
        _ = FetchUrlFormatAsync(normalized, cts, skipDelay: true);
    }

    private void NotifyCanRunChanged()
    {
        OnPropertyChanged(nameof(CanStartRun));
        RunCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
    }

    private void ClearUrlMetadata()
    {
        _cachedUrlMeta = null;
        _selectedFormatOverride = null;
        UrlTitle = null;
        UrlCodec = null;
        UrlBitrate = null;
        UrlSampleRate = null;
        UrlDuration = null;
        UrlFetchError = null;
        FormatPickerItems.Clear();
        IsFormatPickerOpen = false;
        OnPropertyChanged(nameof(HasFormatPicker));
        OnPropertyChanged(nameof(ShowFormatPicker));
        NotifyCanRunChanged();
    }

    private async Task FetchUrlFormatAsync(
        string normalizedUrl,
        CancellationTokenSource cts,
        bool skipDelay = false
    )
    {
        // True only while this fetch is still the active one. A user edit replaces _urlCheckCts
        // with a newer source — any UI mutations after that point belong to the new fetch.
        bool IsActive() => ReferenceEquals(_urlCheckCts, cts);

        IsCheckingUrl = true;
        var ct = cts.Token;
        try
        {
            if (!skipDelay)
                await Task.Delay(800, ct);

            var meta = await _ytAudio.GetAudioFormatInfoAsync(normalizedUrl, _settings, ct);

            // If a newer fetch has taken over, drop this result on the floor.
            if (!IsActive())
                return;

            if (meta is null)
            {
                if (!ct.IsCancellationRequested)
                    UrlFetchError = "Couldn't resolve format — check the URL";
                return;
            }

            _cachedUrlMeta = meta;
            _selectedFormatOverride = null;
            NotifyCanRunChanged();

            UrlTitle = meta.DisplayTitle;
            if (meta.SourceCodec is { Length: > 0 } codec && codec != "none")
                UrlCodec = AudioFormatInfo.PrettyCodec(codec);
            if (meta.SourceBitrateKbps is { } kbps)
                UrlBitrate = $"{kbps:F0} kb/s";
            if (
                meta.AudioFormats?.FirstOrDefault(f => f.FormatId == meta.FormatId) is
                { AudioSampleRate: { } hz }
            )
                UrlSampleRate = $"{hz / 1000.0:F1} kHz";
            if (meta.DurationSeconds is { } dur)
            {
                var ts = TimeSpan.FromSeconds(dur);
                UrlDuration =
                    ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
            }

            FormatPickerItems.Clear();
            if (meta.AudioFormats is { Count: > 1 } formats)
            {
                foreach (var f in formats)
                {
                    var br = f.AverageAudioBitrate ?? f.AverageTotalBitrate;
                    FormatPickerItems.Add(
                        new FormatPickerItem
                        {
                            Format = f,
                            Codec = AudioFormatInfo.PrettyCodec(f.AudioCodec),
                            Bitrate = br is { } b ? $"{b:F0} kb/s" : "—",
                            SampleRate = f.AudioSampleRate is { } fHz
                                ? $"{fHz / 1000.0:F1} kHz"
                                : "—",
                            FormatNote = f.FormatNote ?? "",
                            IsAutoRecommended = f.FormatId == meta.FormatId,
                            IsSelected = f.FormatId == meta.FormatId,
                            IsYtPremium = AudioFormatInfo.IsYouTubePremium(
                                f.FormatId,
                                meta.Extractor
                            ),
                        }
                    );
                }
            }
            OnPropertyChanged(nameof(HasFormatPicker));
            OnPropertyChanged(nameof(ShowFormatPicker));
            OnPropertyChanged(nameof(FormatPickerToggleLabel));
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Only clear the spinner if we're still the active fetch — otherwise we'd
            // clobber the newer fetch's IsCheckingUrl=true.
            if (IsActive())
                IsCheckingUrl = false;
        }
    }

    partial void OnIsUrlInputEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUrlFormatRow));
        OnPropertyChanged(nameof(ShowUrlBlockedMessage));
        NotifyCanRunChanged();
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
        if (TryAddToQueue())
            NavigateToQueueRequested?.Invoke();
    }

    internal static string ExpandPath(string path) =>
        path.StartsWith('~')
            ? Environment.SpecialFolder.UserProfile.GetFolderPath(path.TrimStart('~', '/', '\\'))
            : path;

    public string ExpandedOutputPath => ExpandPath(OutputPath);

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private void AddToQueue() => TryAddToQueue();

    private bool TryAddToQueue()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) && string.IsNullOrWhiteSpace(UrlInput))
            return false;

        var selectedPresets = SelectedPresets();
        var hasUrl = !string.IsNullOrWhiteSpace(UrlInput);

        if (hasUrl)
        {
            // Require a recognisable URL/ID — show inline error if invalid.
            if (_cachedUrlMeta is null && !YtUrlHelper.TryNormalize(UrlInput, out _))
            {
                UrlInputError =
                    "Invalid URL — paste a YouTube link, video ID, or any yt-dlp-supported URL";
                return false;
            }

            UrlInputError = null;

            var sourceUrl =
                _cachedUrlMeta?.SourceUrl
                ?? (YtUrlHelper.TryNormalize(UrlInput, out var n) ? n : UrlInput);

            var preResolvedMeta = _cachedUrlMeta;
            if (preResolvedMeta is not null && _selectedFormatOverride is { } ov)
                preResolvedMeta = preResolvedMeta with
                {
                    MediaUrl = ov.Url!,
                    FormatId = ov.FormatId,
                    SourceCodec = ov.AudioCodec,
                    SourceBitrateKbps = ov.AverageAudioBitrate ?? ov.AverageTotalBitrate,
                };

            _queue.Enqueue(
                new JobRecord(
                    Guid.NewGuid(),
                    InputFilePath: null,
                    SourceUrl: sourceUrl,
                    selectedPresets,
                    ExpandPath(OutputPath),
                    _paths.ModelsDirectory,
                    StemOutputFormat,
                    KeepSourceFile,
                    PreResolvedMeta: preResolvedMeta,
                    ExtractDrums: ExtractDrums
                )
            );

            _urlCheckCts?.Cancel();
            _urlCheckCts = null;
            UrlInput = string.Empty;
            ClearUrlMetadata();
        }
        else
        {
            _queue.Enqueue(
                new JobRecord(
                    Guid.NewGuid(),
                    InputFilePath: InputFilePath,
                    SourceUrl: null,
                    selectedPresets,
                    ExpandPath(OutputPath),
                    _paths.ModelsDirectory,
                    StemOutputFormat,
                    ExtractDrums: ExtractDrums
                )
            );

            InputFilePath = null;
        }

        return true;
    }

    /// <summary>
    /// Queues one job per path. If only one path and no presets are selected, just sets
    /// <see cref="InputFilePath"/> so the user can pick presets before queuing manually.
    /// </summary>
    public void AddFilesToQueue(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0)
            return;

        if (paths.Count == 1 || SelectedCount == 0)
        {
            InputFilePath = paths[0];
            return;
        }

        var presets = SelectedPresets();
        foreach (var path in paths)
        {
            _queue.Enqueue(
                new JobRecord(
                    Guid.NewGuid(),
                    InputFilePath: path,
                    SourceUrl: null,
                    presets,
                    ExpandPath(OutputPath),
                    _paths.ModelsDirectory,
                    StemOutputFormat,
                    ExtractDrums: ExtractDrums
                )
            );
        }

        NavigateToQueueRequested?.Invoke();
    }

    private List<Preset> SelectedPresets() =>
        Categories
            .SelectMany(g => g.Items)
            .Concat(UserPresetItems)
            .Where(i => i.IsSelected)
            .Select(i => i.Preset)
            .ToList();
}

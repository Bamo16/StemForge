using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Json;
using StemForge.Cli.Progress;

namespace StemForge.Cli.Commands;

internal sealed class SeparateCommand : AsyncCommand<SeparateCommand.Settings>
{
    private sealed record SeparateResult(
        string Input,
        bool Succeeded,
        IReadOnlyList<string>? OutputFiles,
        string? Error
    );

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<inputs...>")]
        public string[] Inputs { get; set; } = [];

        [CommandOption("--preset")]
        public string[] PresetIds { get; set; } = [];

        [CommandOption("--model")]
        public string? Model { get; set; }

        [CommandOption("--keep")]
        public string[] KeepStems { get; set; } = [];

        [CommandOption("--output")]
        public string? OutputDir { get; set; }

        [CommandOption("--format")]
        public string? Format { get; set; }

        [CommandOption("--cookies-from-browser")]
        public string? CookiesFromBrowser { get; set; }

        [CommandOption("--keep-source")]
        public bool KeepSource { get; set; }

        [CommandOption("--extract-drums")]
        public bool ExtractDrums { get; set; }

        [CommandOption("--verbose")]
        public bool Verbose { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        using var cancellation = TwoStageCancellation.Install(
            cts,
            message => AppLogger.Warning("cancel", message)
        );

        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var appSettings = provider.GetRequiredService<AppSettings>();
        var appPaths = provider.GetRequiredService<AppPaths>();

        // Apply cookies override before any pipeline work.
        if (!string.IsNullOrWhiteSpace(settings.CookiesFromBrowser))
            appSettings.YtdlpCookiesFromBrowser = settings.CookiesFromBrowser;

        if (!HasWorkToDo(settings.PresetIds, settings.Model, settings.ExtractDrums))
        {
            Console.Error.WriteLine(
                "Error: give at least one of --preset, --model or --extract-drums."
            );
            return 1;
        }

        // Require at least one input.
        if (settings.Inputs is not { Length: > 0 })
        {
            Console.Error.WriteLine("Error: at least one input is required.");
            return 1;
        }

        // Validate all preset IDs up front before any work begins, against the same catalog the
        // `presets` command reports, so the display name it reports is the one written here.
        var catalog = await ResolveCatalogAsync(
            provider.GetRequiredService<PresetCatalogService>(),
            cts.Token
        );

        var presetValidation = ValidatePresets(settings.PresetIds ?? [], catalog);
        if (presetValidation.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: {presetValidation.ErrorMessage}");
            return presetValidation.ExitCode;
        }

        // Only a --model run pays for listing the model catalog.
        var models = settings.Model is { Length: > 0 }
            ? await ResolveModelsAsync(
                provider.GetRequiredService<ModelCatalogService>(),
                cts.Token
            )
            : [];

        var modelValidation = ValidateModel(settings.Model, settings.KeepStems, models);
        if (modelValidation.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: {modelValidation.ErrorMessage}");
            return modelValidation.ExitCode;
        }

        if (modelValidation is { Info: { } modelInfo } && settings.KeepStems is { Length: > 0 })
            await WarnOnUnexpectedKeepStemsAsync(
                provider.GetRequiredService<ModelProfileResolver>(),
                modelInfo,
                settings.KeepStems,
                cts.Token
            );

        IReadOnlyList<Preset> resolvedPresets = modelValidation.Preset is { } modelRun
            ? [.. presetValidation.Presets!, modelRun]
            : presetValidation.Presets!;

        // Resolve format.
        var formatValidation = ValidateFormat(settings.Format, appSettings);
        if (formatValidation.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: {formatValidation.ErrorMessage}");
            return formatValidation.ExitCode;
        }

        var resolvedFormat = formatValidation.ResolvedFormat;

        // Resolve output directory.
        var resolvedOutputDir = string.IsNullOrWhiteSpace(settings.OutputDir)
            ? appPaths.OutputDirectory
            : settings.OutputDir;

        var pipeline = provider.GetRequiredService<SeparationPipeline>();
        var youTubeAudio = provider.GetRequiredService<YouTubeAudioService>();

        int total = settings.Inputs.Length;
        int succeeded = 0;
        int totalFilesWritten = 0;
        bool cancelled = false;
        var results = new List<SeparateResult>(total);

        var display = BatchProgressFactory.Create(
            AnsiConsole.Console,
            settings.Verbose,
            settings.Json
        );
        using var logScope = ProgressLogBridge.Activate(display);

        await display.RunAsync(
            total,
            async () =>
            {
                for (int i = 0; i < settings.Inputs.Length; i++)
                {
                    var input = settings.Inputs[i];
                    int jobNum = i + 1;

                    // Build a display label and create the JobRecord.
                    JobRecord job;
                    string displayLabel;
                    string reportedInput;

                    if (YtUrlHelper.TryNormalize(input, out var normalizedUrl))
                    {
                        // URL input: resolve metadata up front so the input is labelled with its
                        // resolved title (the eventual filename), not the raw URL, and so a bad URL
                        // or network failure is reported before any progress bar is drawn. The
                        // resolved metadata is reused by the pipeline via PreResolvedMeta.
                        Console.Error.WriteLine($"Resolving {normalizedUrl}...");
                        UrlInputResolver.Outcome resolution;
                        try
                        {
                            resolution = await UrlInputResolver.ResolveAsync(
                                youTubeAudio,
                                normalizedUrl,
                                appSettings,
                                cts.Token
                            );
                        }
                        catch (OperationCanceledException)
                        {
                            using var cancelledInput = display.BeginInput(i, total, normalizedUrl);
                            cancelledInput.Complete(InputOutcome.Cancelled, null);
                            results.Add(
                                new SeparateResult(normalizedUrl, false, null, "cancelled")
                            );
                            cancelled = true;
                            break;
                        }

                        if (!resolution.Succeeded)
                        {
                            using var failed = display.BeginInput(i, total, normalizedUrl);
                            var reason = resolution.FailureReason ?? "resolution failed";
                            failed.Complete(InputOutcome.Failed, reason);
                            results.Add(new SeparateResult(normalizedUrl, false, null, reason));
                            continue;
                        }

                        reportedInput = normalizedUrl;
                        displayLabel = resolution.Title!;
                        job = new JobRecord(
                            Id: Guid.NewGuid(),
                            InputFilePath: null,
                            SourceUrl: normalizedUrl,
                            Presets: resolvedPresets,
                            OutputDir: resolvedOutputDir,
                            ModelsDir: appPaths.ModelsDirectory,
                            StemOutputFormat: resolvedFormat,
                            KeepSourceFile: settings.KeepSource,
                            PreResolvedMeta: resolution.Meta,
                            ExtractDrums: settings.ExtractDrums
                        );
                    }
                    else
                    {
                        // Local file input — validate existence before this specific job.
                        var resolvedPath = Path.GetFullPath(input);
                        if (!File.Exists(resolvedPath))
                        {
                            using var missing = display.BeginInput(
                                i,
                                total,
                                Path.GetFileName(resolvedPath)
                            );
                            missing.Complete(
                                InputOutcome.Failed,
                                $"Input file not found: {resolvedPath}"
                            );
                            results.Add(
                                new SeparateResult(
                                    resolvedPath,
                                    false,
                                    null,
                                    $"Input file not found: {resolvedPath}"
                                )
                            );
                            continue;
                        }

                        reportedInput = resolvedPath;
                        displayLabel = Path.GetFileName(resolvedPath);
                        job = new JobRecord(
                            Id: Guid.NewGuid(),
                            InputFilePath: resolvedPath,
                            SourceUrl: null,
                            Presets: resolvedPresets,
                            OutputDir: resolvedOutputDir,
                            ModelsDir: appPaths.ModelsDirectory,
                            StemOutputFormat: resolvedFormat,
                            KeepSourceFile: settings.KeepSource,
                            ExtractDrums: settings.ExtractDrums
                        );
                    }

                    using var inputProgress = display.BeginInput(i, total, displayLabel);

                    var progress = JobProgressReporter.For(inputProgress);

                    try
                    {
                        var outputFiles = await pipeline.RunAsync(job, progress, cts.Token);
                        succeeded++;
                        totalFilesWritten += outputFiles.Count;
                        inputProgress.Complete(
                            InputOutcome.Succeeded,
                            "file".ToQuantity(outputFiles.Count)
                        );
                        results.Add(new SeparateResult(reportedInput, true, outputFiles, null));
                    }
                    catch (OperationCanceledException)
                    {
                        inputProgress.Complete(InputOutcome.Cancelled, null);
                        results.Add(new SeparateResult(reportedInput, false, null, "cancelled"));
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        inputProgress.Complete(InputOutcome.Failed, ex.Message);
                        results.Add(new SeparateResult(reportedInput, false, null, ex.Message));
                    }
                }
            }
        );

        if (settings.Json)
        {
            CliJson.Write(results);
            return cancelled ? (succeeded > 0 ? 2 : 1)
                : succeeded == 0 ? 1
                : succeeded == total ? 0
                : 2;
        }

        // Print end-of-run summary.
        if (cancelled)
        {
            if (succeeded > 0)
            {
                Console.Error.WriteLine(
                    $"Cancelled after {succeeded}/{total} succeeded. {"file".ToQuantity(totalFilesWritten)} written to {resolvedOutputDir}"
                );
                return 2;
            }

            return 1;
        }

        if (succeeded == 0)
        {
            Console.Error.WriteLine($"Error. All {total} inputs failed.");
            return 1;
        }

        Console.WriteLine(
            $"Done. {succeeded}/{total} succeeded. {"file".ToQuantity(totalFilesWritten)} written to {resolvedOutputDir}"
        );

        return succeeded == total ? 0 : 2;
    }

    /// <summary>
    /// Whether the invocation asks for any separation at all; <c>--model</c> and
    /// <c>--extract-drums</c> are runs in their own right, not modifiers on a preset.
    /// </summary>
    internal static bool HasWorkToDo(string[]? presetIds, string? model, bool extractDrums) =>
        presetIds is { Length: > 0 } || !string.IsNullOrWhiteSpace(model) || extractDrums;

    /// <summary>
    /// Resolves <c>--model</c> and <c>--keep</c> into a single-model run, checking the file name
    /// against <paramref name="catalog"/> unless it is empty (unreadable). Kept stems are not checked.
    /// </summary>
    internal static ModelValidationOutcome ValidateModel(
        string? model,
        string[]? keepStems,
        IReadOnlyList<ModelInfo> catalog
    )
    {
        IReadOnlyList<string> keep =
        [
            .. (keepStems ?? []).Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        if (string.IsNullOrWhiteSpace(model))
            return keep.Count == 0
                ? ModelValidationOutcome.Ok(null, null)
                : ModelValidationOutcome.Fail(
                    "--keep applies to a --model run; a built-in preset keeps a fixed stem."
                );

        if (catalog.Count == 0)
            return ModelValidationOutcome.Ok(Preset.SingleModel(model, keep), null);

        if (
            catalog.FirstOrDefault(info =>
                info.Filename.Equals(model, StringComparison.OrdinalIgnoreCase)
            )
            is not { } match
        )
            return ModelValidationOutcome.Fail(UnknownModelMessage(model, catalog));

        return ModelValidationOutcome.Ok(Preset.SingleModel(match.Filename, keep), match);
    }

    /// <summary>
    /// The warning for kept stems the model profile does not predict, or null when there are none
    /// or the profile names no stems. Advisory only (ADR 0010), so it never stops the run.
    /// </summary>
    internal static string? KeepStemWarning(ModelProfile profile, IReadOnlyList<string> keepStems)
    {
        var unexpected = keepStems
            .Where(kept =>
                !profile.Stems.Any(stem =>
                    stem.Name.Equals(kept, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();

        return profile.IsUnknown || unexpected is []
            ? null
            : $"{profile.Filename} is expected to write {string.Join(", ", profile.Stems.Select(stem => stem.Name))}, "
                + $"not {string.Join(", ", unexpected)}. Running anyway; the input fails if no kept stem is written.";
    }

    private static string UnknownModelMessage(string model, IReadOnlyList<ModelInfo> catalog)
    {
        var name = Path.GetFileNameWithoutExtension(model);
        var near = catalog
            .Where(info => info.Filename.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(info => info.Filename)
            .Take(5)
            .ToList();

        return near is []
            ? $"Unknown model '{model}'. Give the model's file name, for example MDX23C-DrumSep-aufr33-jarredou.ckpt."
            : $"Unknown model '{model}'. Did you mean {string.Join(", ", near)}?";
    }

    private static async Task WarnOnUnexpectedKeepStemsAsync(
        ModelProfileResolver resolver,
        ModelInfo model,
        string[] keepStems,
        CancellationToken ct
    )
    {
        if (KeepStemWarning(await resolver.ResolveAsync(model, ct), keepStems) is { } warning)
            Console.Error.WriteLine($"Warning: {warning}");
    }

    /// <summary>
    /// The audio-separator model catalog, or empty when it cannot be read, in which case
    /// <c>--model</c> goes unchecked and the driver reports an unknown name.
    /// </summary>
    private static async Task<IReadOnlyList<ModelInfo>> ResolveModelsAsync(
        ModelCatalogService catalog,
        CancellationToken ct
    )
    {
        try
        {
            return await catalog.ListModelsAsync(ct: ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(
                "model",
                $"Model catalog unavailable ({ex.Message}); --model is not checked."
            );
            return [];
        }
    }

    /// <summary>
    /// Resolves the catalog to validate against: the live audio-separator catalog where
    /// <see cref="PresetCatalogService"/> can read it, and the static built-ins otherwise.
    ///
    /// Sharing the live catalog with the <c>presets</c> command is what makes the display name that
    /// command reports the name this one writes into provenance and output filenames for the same
    /// id. The fallback is not optional though: a missing or broken toolchain must not make
    /// <c>separate</c> start refusing presets it has always accepted, so both an empty result (how
    /// the service signals a failed read) and an outright throw fall back rather than propagate.
    /// </summary>
    private static async Task<IReadOnlyList<Preset>> ResolveCatalogAsync(
        PresetCatalogService catalog,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await catalog.ListPresetsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(
                "preset",
                $"Live preset catalog unavailable ({ex.Message}); using built-in catalog."
            );
            return [];
        }
    }

    /// <summary>
    /// Validates all preset IDs up front. Returns failure on the first unknown preset. Resolves
    /// against <paramref name="catalog"/>, falling back to <see cref="PresetCatalog.BuiltIn"/> when
    /// it is empty or omitted.
    /// </summary>
    internal static PresetValidationOutcome ValidatePresets(
        string[] presetIds,
        IReadOnlyList<Preset>? catalog = null
    )
    {
        var source = catalog is { Count: > 0 } live ? live : PresetCatalog.BuiltIn;

        var presets = new List<Preset>(presetIds.Length);
        foreach (var id in presetIds)
        {
            var preset = source.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)
            );
            if (preset is null)
            {
                var validIds = string.Join(", ", source.Select(p => p.Id));
                return PresetValidationOutcome.Fail(
                    $"Unknown preset '{id}'. Valid presets: {validIds}"
                );
            }

            presets.Add(preset);
        }

        return PresetValidationOutcome.Ok(presets);
    }

    /// <summary>
    /// Validates and resolves the audio format string.
    /// </summary>
    internal static FormatValidationOutcome ValidateFormat(
        string? formatStr,
        AppSettings appSettings
    )
    {
        AudioFormat resolvedFormat;
        if (string.IsNullOrWhiteSpace(formatStr))
        {
            resolvedFormat = appSettings.DefaultAudioFormat;
        }
        else if (Enum.TryParse<AudioFormat>(formatStr, ignoreCase: true, out var parsedFormat))
        {
            resolvedFormat = parsedFormat;
        }
        else
        {
            var validFormats = string.Join(", ", Enum.GetNames<AudioFormat>());
            return FormatValidationOutcome.Fail(
                $"Unknown format '{formatStr}'. Valid formats: {validFormats}"
            );
        }

        return FormatValidationOutcome.Ok(resolvedFormat);
    }

    /// <summary>
    /// Validates the command inputs independently of DI and console I/O.
    /// Returns a <see cref="ValidationOutcome"/> describing the result.
    /// </summary>
    internal static ValidationOutcome Validate(
        string inputFile,
        string presetId,
        string? formatStr,
        string? outputDirOverride,
        AppSettings appSettings,
        AppPaths appPaths
    )
    {
        // Validate preset.
        var presetValidation = ValidatePresets([presetId]);
        if (presetValidation.ExitCode != 0)
            return ValidationOutcome.Fail(presetValidation.ErrorMessage!);

        var preset = presetValidation.Presets![0];

        // Validate input file (URLs are not validated here — only local files).
        if (!YtUrlHelper.TryNormalize(inputFile, out _))
        {
            var resolvedInput = Path.GetFullPath(inputFile);
            if (!File.Exists(resolvedInput))
                return ValidationOutcome.Fail($"Input file not found: {resolvedInput}");
        }

        // Resolve output directory.
        var resolvedOutputDir = string.IsNullOrWhiteSpace(outputDirOverride)
            ? appPaths.OutputDirectory
            : outputDirOverride;

        // Resolve format.
        var formatValidation = ValidateFormat(formatStr, appSettings);
        if (formatValidation.ExitCode != 0)
            return ValidationOutcome.Fail(formatValidation.ErrorMessage!);

        return ValidationOutcome.Ok(preset, resolvedOutputDir, formatValidation.ResolvedFormat);
    }

    /// <summary>Result of <see cref="ValidatePresets"/>.</summary>
    internal sealed record PresetValidationOutcome(
        int ExitCode,
        string? ErrorMessage,
        IReadOnlyList<Preset>? Presets
    )
    {
        internal static PresetValidationOutcome Fail(string message) => new(1, message, null);

        internal static PresetValidationOutcome Ok(IReadOnlyList<Preset> presets) =>
            new(0, null, presets);
    }

    /// <summary>Result of <see cref="ValidateModel"/>; <see cref="Info"/> is null when the catalog was unreadable.</summary>
    internal sealed record ModelValidationOutcome(
        int ExitCode,
        string? ErrorMessage,
        Preset? Preset,
        ModelInfo? Info
    )
    {
        internal static ModelValidationOutcome Fail(string message) => new(1, message, null, null);

        internal static ModelValidationOutcome Ok(Preset? preset, ModelInfo? info) =>
            new(0, null, preset, info);
    }

    /// <summary>Result of <see cref="ValidateFormat"/>.</summary>
    internal sealed record FormatValidationOutcome(
        int ExitCode,
        string? ErrorMessage,
        AudioFormat ResolvedFormat
    )
    {
        internal static FormatValidationOutcome Fail(string message) => new(1, message, default);

        internal static FormatValidationOutcome Ok(AudioFormat format) => new(0, null, format);
    }

    /// <summary>Result of <see cref="Validate"/>.</summary>
    internal sealed record ValidationOutcome(
        int ExitCode,
        string? ErrorMessage,
        Preset? Preset,
        string? ResolvedOutputDir,
        AudioFormat ResolvedFormat
    )
    {
        internal static ValidationOutcome Fail(string message) =>
            new(1, message, null, null, default);

        internal static ValidationOutcome Ok(Preset preset, string outputDir, AudioFormat format) =>
            new(0, null, preset, outputDir, format);
    }
}

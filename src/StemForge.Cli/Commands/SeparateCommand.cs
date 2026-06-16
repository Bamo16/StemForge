using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Progress;
using StemForge.Core.Helpers;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Cli.Commands;

internal sealed class SeparateCommand : AsyncCommand<SeparateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<inputs...>")]
        public string[] Inputs { get; set; } = [];

        [CommandOption("--preset")]
        public string[] PresetIds { get; set; } = [];

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

        // Require at least one preset.
        if (settings.PresetIds is not { Length: > 0 })
        {
            Console.Error.WriteLine("Error: --preset is required.");
            return 1;
        }

        // Require at least one input.
        if (settings.Inputs is not { Length: > 0 })
        {
            Console.Error.WriteLine("Error: at least one input is required.");
            return 1;
        }

        // Validate all preset IDs up front before any work begins.
        var presetValidation = ValidatePresets(settings.PresetIds);
        if (presetValidation.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: {presetValidation.ErrorMessage}");
            return presetValidation.ExitCode;
        }

        var resolvedPresets = presetValidation.Presets!;

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

        var display = BatchProgressFactory.Create(AnsiConsole.Console, settings.Verbose);
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
                            cancelled = true;
                            break;
                        }

                        if (!resolution.Succeeded)
                        {
                            using var failed = display.BeginInput(i, total, normalizedUrl);
                            failed.Complete(
                                InputOutcome.Failed,
                                resolution.FailureReason ?? "resolution failed"
                            );
                            continue;
                        }

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
                            continue;
                        }

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
                            $"{outputFiles.Count} file(s)"
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        inputProgress.Complete(InputOutcome.Cancelled, null);
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        inputProgress.Complete(InputOutcome.Failed, ex.Message);
                    }
                }
            }
        );

        // Print end-of-run summary.
        if (cancelled)
        {
            if (succeeded > 0)
            {
                Console.Error.WriteLine(
                    $"Cancelled after {succeeded}/{total} succeeded. {totalFilesWritten} file(s) written to {resolvedOutputDir}"
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
            $"Done. {succeeded}/{total} succeeded. {totalFilesWritten} file(s) written to {resolvedOutputDir}"
        );

        return succeeded == total ? 0 : 2;
    }

    /// <summary>
    /// Validates all preset IDs up front. Returns failure on the first unknown preset.
    /// </summary>
    internal static PresetValidationOutcome ValidatePresets(string[] presetIds)
    {
        var presets = new List<Preset>(presetIds.Length);
        foreach (var id in presetIds)
        {
            var preset = PresetCatalog.BuiltIn.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)
            );
            if (preset is null)
            {
                var validIds = string.Join(", ", PresetCatalog.BuiltIn.Select(p => p.Id));
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

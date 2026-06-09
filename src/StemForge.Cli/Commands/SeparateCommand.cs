using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Cli.Commands;

internal sealed class SeparateCommand : AsyncCommand<SeparateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<inputFile>")]
        public string InputFile { get; set; } = "";

        [CommandOption("--preset")]
        public string? PresetId { get; set; }

        [CommandOption("--output")]
        public string? OutputDir { get; set; }

        [CommandOption("--format")]
        public string? Format { get; set; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var appSettings = provider.GetRequiredService<AppSettings>();
        var appPaths = provider.GetRequiredService<AppPaths>();

        // Validate preset.
        if (string.IsNullOrWhiteSpace(settings.PresetId))
        {
            Console.Error.WriteLine("Error: --preset is required.");
            return 1;
        }

        var validationResult = Validate(
            settings.InputFile,
            settings.PresetId,
            settings.Format,
            settings.OutputDir,
            appSettings,
            appPaths
        );

        if (validationResult.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: {validationResult.ErrorMessage}");
            return validationResult.ExitCode;
        }

        var preset = validationResult.Preset!;
        var resolvedOutputDir = validationResult.ResolvedOutputDir!;
        var resolvedFormat = validationResult.ResolvedFormat;

        var inputPath = Path.GetFullPath(settings.InputFile);
        var inputFileName = Path.GetFileName(inputPath);

        Console.WriteLine($"Separating '{inputFileName}' using {preset.DisplayName} ...");

        var pipeline = provider.GetRequiredService<SeparationPipeline>();

        var progress = new Progress<JobUpdate>(update =>
        {
            if (update.Phase == "progress" && update.RunLabel is not null)
            {
                Console.WriteLine(
                    $"[{update.RunIndex + 1}/{update.RunCount}] {update.RunLabel}: {update.OverallPercent}%"
                );
            }
        });

        var job = new JobRecord(
            Id: Guid.NewGuid(),
            InputFilePath: inputPath,
            SourceUrl: null,
            Presets: [preset],
            OutputDir: resolvedOutputDir,
            ModelsDir: appPaths.ModelsDirectory,
            StemOutputFormat: resolvedFormat
        );

        IReadOnlyList<string> outputFiles;
        try
        {
            outputFiles = await pipeline.RunAsync(job, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Separation cancelled.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Done. {outputFiles.Count} file(s) written to {resolvedOutputDir}");
        return 0;
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
        var preset = PresetCatalog.BuiltIn.FirstOrDefault(p =>
            string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase)
        );
        if (preset is null)
        {
            var validIds = string.Join(", ", PresetCatalog.BuiltIn.Select(p => p.Id));
            return ValidationOutcome.Fail(
                $"Unknown preset '{presetId}'. Valid presets: {validIds}"
            );
        }

        // Validate input file.
        var resolvedInput = Path.GetFullPath(inputFile);
        if (!File.Exists(resolvedInput))
        {
            return ValidationOutcome.Fail($"Input file not found: {resolvedInput}");
        }

        // Resolve output directory.
        var resolvedOutputDir = string.IsNullOrWhiteSpace(outputDirOverride)
            ? appPaths.OutputDirectory
            : outputDirOverride;

        // Resolve format.
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
            return ValidationOutcome.Fail(
                $"Unknown format '{formatStr}'. Valid formats: {validFormats}"
            );
        }

        return ValidationOutcome.Ok(preset, resolvedOutputDir, resolvedFormat);
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

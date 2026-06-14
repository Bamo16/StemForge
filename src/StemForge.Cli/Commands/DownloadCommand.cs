using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Progress;
using StemForge.Core.Helpers;
using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Cli.Commands;

/// <summary>
/// Fetches audio from one or more URLs without separating. Each download is written to the
/// output directory in the requested format with metadata, provenance, and thumbnail applied.
/// Shares the batch / summary / exit-code semantics of the separate command: continue-on-failure
/// across inputs, an end-of-run summary, and exit codes 0 (all succeeded), 2 (partial), 1 (all failed).
/// </summary>
internal sealed class DownloadCommand : AsyncCommand<DownloadCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<urls...>")]
        public string[] Urls { get; set; } = [];

        [CommandOption("--output")]
        public string? OutputDir { get; set; }

        [CommandOption("--format")]
        public string? Format { get; set; }

        [CommandOption("--cookies-from-browser")]
        public string? CookiesFromBrowser { get; set; }

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

        // Require at least one URL.
        if (settings.Urls is not { Length: > 0 })
        {
            Console.Error.WriteLine("Error: at least one URL is required.");
            return 1;
        }

        // Resolve format (defaults to saved settings).
        var formatValidation = SeparateCommand.ValidateFormat(settings.Format, appSettings);
        if (formatValidation.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: {formatValidation.ErrorMessage}");
            return formatValidation.ExitCode;
        }

        var resolvedFormat = formatValidation.ResolvedFormat;

        // Resolve output directory (defaults to saved settings).
        var resolvedOutputDir = string.IsNullOrWhiteSpace(settings.OutputDir)
            ? appPaths.OutputDirectory
            : settings.OutputDir;

        var pipeline = provider.GetRequiredService<SeparationPipeline>();

        int total = settings.Urls.Length;
        int succeeded = 0;
        int totalFilesWritten = 0;
        bool cancelled = false;

        var display = BatchProgressFactory.Create(AnsiConsole.Console, settings.Verbose);
        using var logScope = ProgressLogBridge.Activate(display);

        await display.RunAsync(
            total,
            async () =>
            {
                for (int i = 0; i < settings.Urls.Length; i++)
                {
                    var input = settings.Urls[i];

                    // Download only accepts URLs; a local file path has nothing to download.
                    if (!YtUrlHelper.TryNormalize(input, out var normalizedUrl))
                    {
                        using var invalid = display.BeginInput(i, total, input);
                        invalid.Complete(InputOutcome.Failed, $"not a recognized URL: {input}");
                        continue;
                    }

                    var job = new JobRecord(
                        Id: Guid.NewGuid(),
                        InputFilePath: null,
                        SourceUrl: normalizedUrl,
                        Presets: [],
                        OutputDir: resolvedOutputDir,
                        ModelsDir: appPaths.ModelsDirectory,
                        StemOutputFormat: resolvedFormat
                    );

                    using var inputProgress = display.BeginInput(i, total, normalizedUrl);

                    var progress = JobProgressReporter.For(inputProgress);

                    try
                    {
                        var path = await pipeline.DownloadOnlyAsync(job, progress, cts.Token);
                        succeeded++;
                        totalFilesWritten++;
                        inputProgress.Complete(InputOutcome.Succeeded, Path.GetFileName(path));
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
}

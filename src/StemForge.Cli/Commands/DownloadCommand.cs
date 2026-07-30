using System.Globalization;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Json;
using StemForge.Cli.Progress;

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

        /// <summary>
        /// Pins the source format to fetch. Distinct from --format, which is the output
        /// encoding: this selects what is downloaded, that selects what it is written as.
        /// </summary>
        [CommandOption("--format-id")]
        public string? FormatId { get; set; }

        /// <summary>Resolve and report the candidate source formats without downloading.</summary>
        [CommandOption("--list-formats")]
        public bool ListFormats { get; set; }

        [CommandOption("--cookies-from-browser")]
        public string? CookiesFromBrowser { get; set; }

        [CommandOption("--verbose")]
        public bool Verbose { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    /// <summary>
    /// One row of <c>--json</c> output. The acquisition fields are additive: consumers that only
    /// read input/succeeded/path/error keep working, while those that care about acquisition
    /// quality can assert it per input instead of reading provenance tags off the finished file.
    /// They are null when the input never resolved.
    /// </summary>
    private sealed record DownloadResult(
        string Input,
        bool Succeeded,
        string? Path,
        string? Error,
        string? FormatId = null,
        string? Codec = null,
        double? BitrateKbps = null,
        bool? IsPremium = null,
        bool? PremiumShortfall = null
    );

    /// <summary>One candidate source format, as reported by <c>--list-formats --json</c>.</summary>
    private sealed record FormatCandidate(
        string? FormatId,
        string? Codec,
        double? BitrateKbps,
        int? SampleRateHz,
        string? Note,
        bool IsPremium,
        bool IsAutoSelected
    );

    /// <summary>One input's full candidate list, as reported by <c>--list-formats --json</c>.</summary>
    private sealed record FormatListResult(
        string Input,
        bool Succeeded,
        string? Error,
        string? Title = null,
        string? Artist = null,
        double? DurationSeconds = null,
        bool? PremiumShortfall = null,
        IReadOnlyList<FormatCandidate>? Formats = null
    );

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

        // --list-formats resolves and reports without downloading, so it needs neither an output
        // format nor an output directory, and it bypasses the batch progress display entirely to
        // keep stdout clean for scripted callers.
        if (settings.ListFormats)
        {
            return await ListFormatsAsync(
                settings,
                appSettings,
                provider.GetRequiredService<YouTubeAudioService>(),
                cts.Token
            );
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
        var youTubeAudio = provider.GetRequiredService<YouTubeAudioService>();

        // Inferred once: the expectation comes from a cookie source being configured, in settings
        // or via the --cookies-from-browser override applied above.
        var premiumExpected = PremiumExpectation.IsHeldBy(appSettings);

        int total = settings.Urls.Length;
        int succeeded = 0;
        int totalFilesWritten = 0;
        bool cancelled = false;
        var results = new List<DownloadResult>(total);

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
                for (int i = 0; i < settings.Urls.Length; i++)
                {
                    var input = settings.Urls[i];

                    // Download only accepts URLs; a local file path has nothing to download.
                    if (!YtUrlHelper.TryNormalize(input, out var normalizedUrl))
                    {
                        using var invalid = display.BeginInput(i, total, input);
                        invalid.Complete(InputOutcome.Failed, $"not a recognized URL: {input}");
                        results.Add(
                            new DownloadResult(input, false, null, $"not a recognized URL: {input}")
                        );
                        continue;
                    }

                    // Resolve metadata up front so the input is labelled with its resolved title
                    // (the eventual filename), not the raw URL, and so a bad URL or network failure
                    // is reported before any progress bar is drawn. The resolved metadata is reused
                    // by the pipeline via PreResolvedMeta.
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
                        results.Add(new DownloadResult(normalizedUrl, false, null, "cancelled"));
                        cancelled = true;
                        break;
                    }

                    if (!resolution.Succeeded)
                    {
                        using var failed = display.BeginInput(i, total, normalizedUrl);
                        var reason = resolution.FailureReason ?? "resolution failed";
                        failed.Complete(InputOutcome.Failed, reason);
                        results.Add(new DownloadResult(normalizedUrl, false, null, reason));
                        continue;
                    }

                    var meta = resolution.Meta!;

                    // Pin the source format when asked. A source that does not offer it fails
                    // this input rather than silently falling back, which is the substitution
                    // pinning exists to prevent. Checked before the download, so a miss costs
                    // nothing but the resolve.
                    if (!string.IsNullOrWhiteSpace(settings.FormatId))
                    {
                        if (!meta.TryPinFormat(settings.FormatId, out var pinnedMeta))
                        {
                            var offered = meta.AudioFormats is { Count: > 0 } list
                                ? string.Join(", ", list.Select(f => f.FormatId))
                                : "none";
                            var reason =
                                $"format {settings.FormatId} is not offered by this source (offered: {offered})";
                            using var pinFailed = display.BeginInput(i, total, normalizedUrl);
                            pinFailed.Complete(InputOutcome.Failed, reason);
                            results.Add(new DownloadResult(normalizedUrl, false, null, reason));
                            continue;
                        }

                        meta = pinnedMeta;
                    }

                    // Fires regardless of the flags above, so a dead browser session is still
                    // reported when the caller passed nothing at all.
                    var premiumStatus = PremiumExpectation.Evaluate(meta, premiumExpected);
                    if (PremiumAdvisory.For(premiumStatus) is { } advisory)
                        Console.Error.WriteLine(advisory);

                    var job = new JobRecord(
                        Id: Guid.NewGuid(),
                        InputFilePath: null,
                        SourceUrl: normalizedUrl,
                        Presets: [],
                        OutputDir: resolvedOutputDir,
                        ModelsDir: appPaths.ModelsDirectory,
                        StemOutputFormat: resolvedFormat,
                        PreResolvedMeta: meta
                    );

                    using var inputProgress = display.BeginInput(i, total, resolution.Title!);

                    var progress = JobProgressReporter.For(inputProgress);

                    try
                    {
                        var path = await pipeline.DownloadOnlyAsync(job, progress, cts.Token);
                        succeeded++;
                        totalFilesWritten++;
                        inputProgress.Complete(InputOutcome.Succeeded, Path.GetFileName(path));
                        results.Add(
                            new DownloadResult(
                                normalizedUrl,
                                true,
                                path,
                                null,
                                FormatId: meta.FormatId,
                                Codec: meta.SourceCodec,
                                BitrateKbps: meta.SourceBitrateKbps,
                                IsPremium: meta.SelectedFormatIsPremium,
                                PremiumShortfall: premiumStatus
                                    is not (PremiumStatus.NotApplicable or PremiumStatus.Premium)
                            )
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        inputProgress.Complete(InputOutcome.Cancelled, null);
                        results.Add(new DownloadResult(normalizedUrl, false, null, "cancelled"));
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        inputProgress.Complete(InputOutcome.Failed, ex.Message);
                        results.Add(new DownloadResult(normalizedUrl, false, null, ex.Message));
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
    /// Resolves each input and reports its candidate source formats without downloading anything.
    /// Keeps the batch semantics of a real run so a loop over candidate URLs behaves predictably:
    /// continue on failure, exit 0 (all resolved) / 2 (partial) / 1 (none).
    /// </summary>
    private static async Task<int> ListFormatsAsync(
        Settings settings,
        AppSettings appSettings,
        YouTubeAudioService youTubeAudio,
        CancellationToken ct
    )
    {
        var premiumExpected = PremiumExpectation.IsHeldBy(appSettings);
        var results = new List<FormatListResult>(settings.Urls.Length);
        int resolved = 0;

        foreach (var input in settings.Urls)
        {
            if (!YtUrlHelper.TryNormalize(input, out var normalizedUrl))
            {
                results.Add(new FormatListResult(input, false, $"not a recognized URL: {input}"));
                continue;
            }

            UrlInputResolver.Outcome outcome;
            try
            {
                outcome = await UrlInputResolver.ResolveAsync(
                    youTubeAudio,
                    normalizedUrl,
                    appSettings,
                    ct
                );
            }
            catch (OperationCanceledException)
            {
                results.Add(new FormatListResult(normalizedUrl, false, "cancelled"));
                break;
            }

            if (!outcome.Succeeded || outcome.Meta is not { } meta)
            {
                results.Add(
                    new FormatListResult(
                        normalizedUrl,
                        false,
                        outcome.FailureReason ?? "resolution failed"
                    )
                );
                continue;
            }

            resolved++;
            var status = PremiumExpectation.Evaluate(meta, premiumExpected);
            var formats = meta.AudioFormats ?? [];
            var candidates = formats
                .Select(f => new FormatCandidate(
                    f.FormatId,
                    AudioFormatInfo.PrettyCodec(f.AudioCodec),
                    f.AverageAudioBitrate ?? f.AverageTotalBitrate,
                    f.AudioSampleRate,
                    f.FormatNote,
                    PremiumFormats.IsPremium(f, formats, meta.Extractor),
                    f.FormatId == meta.FormatId
                ))
                .ToList();

            results.Add(
                new FormatListResult(
                    normalizedUrl,
                    true,
                    null,
                    meta.Title,
                    meta.Artist,
                    meta.DurationSeconds,
                    status is not (PremiumStatus.NotApplicable or PremiumStatus.Premium),
                    candidates
                )
            );

            if (!settings.Json)
                WriteFormatTable(meta, candidates);

            if (PremiumAdvisory.For(status) is { } advisory)
                Console.Error.WriteLine(advisory);
        }

        if (settings.Json)
            CliJson.Write(results);

        return resolved == 0 ? 1
            : resolved == settings.Urls.Length ? 0
            : 2;
    }

    /// <summary>
    /// Human-readable candidate table. Deliberately written to stdout rather than routed through
    /// AppLogger's tagged format, so the output is pipeable.
    /// </summary>
    private static void WriteFormatTable(
        YtDlpMetadata meta,
        IReadOnlyList<FormatCandidate> candidates
    )
    {
        Console.WriteLine(meta.DisplayTitle);
        Console.WriteLine(
            $"  {"ID", -6} {"CODEC", -8} {"KBPS", 6} {"KHZ", 6}  {"PREMIUM", -7}  NOTE"
        );

        foreach (var c in candidates)
        {
            var marker = c.IsAutoSelected ? ">" : " ";
            var kbps = c.BitrateKbps?.ToString("F0", CultureInfo.InvariantCulture) ?? "?";
            var khz = c.SampleRateHz is { } hz
                ? (hz / 1000.0).ToString("F1", CultureInfo.InvariantCulture)
                : "?";
            var premium = c.IsPremium ? "yes" : "";
            Console.WriteLine(
                $"{marker} {c.FormatId, -6} {c.Codec, -8} {kbps, 6} {khz, 6}  {premium, -7}  {c.Note}"
            );
        }

        Console.WriteLine("  (> marks the format that would be downloaded)");
    }
}

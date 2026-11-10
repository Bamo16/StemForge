using System.Text.RegularExpressions;

namespace StemForge.Core.Tooling;

/// <summary>
/// One tool as <c>stemforge-cli tools</c> reports it. <paramref name="Kind"/> is the tool's CLI name
/// (<c>ffmpeg</c>, <c>ffprobe</c>, <c>yt-dlp</c>, <c>deno</c>, <c>uv</c>, <c>audio-separator</c>).
/// <paramref name="Path"/> is the path StemForge would run, present or not. <paramref name="Release"/>
/// is the bundled release pin; it is null for an overridden tool and for tools with no pin.
/// </summary>
public sealed record ToolReport(
    string Kind,
    string Path,
    bool Installed,
    bool Overridden,
    string? Version,
    string? Release
);

public sealed partial class ToolReportService(
    SetupDetector detector,
    AppPaths paths,
    ToolInstaller installer,
    IProcessRunner runner,
    PlatformInfo platform
)
{
    private const string FfprobeKind = "ffprobe";

    private readonly SetupDetector _detector = detector;
    private readonly AppPaths _paths = paths;
    private readonly ToolInstaller _installer = installer;
    private readonly IProcessRunner _runner = runner;
    private readonly PlatformInfo _platform = platform;

    [GeneratedRegex(@"ffprobe version\s+(?<Version>\S+)")]
    private static partial Regex FfprobeVersion { get; }

    /// <summary>
    /// Reports every tool available on this platform, in catalog order with ffprobe after ffmpeg.
    /// With <paramref name="ensure"/>, first fetches each bundled tool that is missing and not
    /// overridden; uv and audio-separator are never fetched here.
    /// </summary>
    public async Task<IReadOnlyList<ToolReport>> ReportAsync(
        bool ensure,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        var report = await ProbeAsync();

        if (ensure)
        {
            var missing = ToolCatalog
                .AvailableFor(_platform)
                .Where(tool =>
                    tool.InstallStrategy is BundledFetch && !_paths.IsOverridden(tool.Kind)
                )
                .Where(tool => report.Any(r => IsMissing(r, tool)))
                .ToList();

            foreach (var tool in missing)
                await _installer.InstallAsync(tool, new InstallOptions(), progress, ct);

            if (missing.Count > 0)
                report = await ProbeAsync();
        }

        return report;
    }

    private static bool IsMissing(ToolReport report, Tool tool) =>
        !report.Installed
        && (
            report.Kind == tool.CliName
            // ffprobe comes in ffmpeg's archive, so a missing one means ffmpeg wants fetching.
            || (tool.Kind is ToolKind.Ffmpeg && report.Kind == FfprobeKind)
        );

    private async Task<IReadOnlyList<ToolReport>> ProbeAsync()
    {
        var states = await _detector.DetectAsync();
        var ffprobe = await ProbeFfprobeAsync();
        var reports = new List<ToolReport>();

        foreach (var tool in ToolCatalog.AvailableFor(_platform))
        {
            var state = states.Single(s => s.Kind == tool.Kind);
            var overridden = _paths.IsOverridden(tool.Kind);

            reports.Add(
                new ToolReport(
                    tool.CliName,
                    _paths.PathFor(tool.Kind),
                    state.Found,
                    overridden,
                    state.Version,
                    overridden ? null : ToolCatalog.BundledRelease(tool.Kind)
                )
            );

            if (tool.Kind is ToolKind.Ffmpeg)
                reports.Add(ffprobe);
        }

        return reports;
    }

    private async Task<ToolReport> ProbeFfprobeAsync()
    {
        var overridden = _paths.IsOverridden(ToolKind.Ffmpeg);
        var release = overridden ? null : ToolCatalog.BundledRelease(ToolKind.Ffmpeg);

        try
        {
            var result = await _runner.RunAsync(_paths.Ffprobe, ["-version"]);
            var version = FfprobeVersion.Match(result.Output).Groups["Version"]
                is { Success: true, Value: var found }
                ? found
                : null;

            return new ToolReport(FfprobeKind, _paths.Ffprobe, true, overridden, version, release);
        }
        catch
        {
            return new ToolReport(FfprobeKind, _paths.Ffprobe, false, overridden, null, release);
        }
    }
}

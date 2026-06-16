using StemForge.Core.Models;

namespace StemForge.Core.Services;

/// <summary>Progress update from an install: a phase/log message, optionally with byte counts.</summary>
public sealed record InstallProgress(
    string Message,
    long? BytesDownloaded = null,
    long? TotalBytes = null
)
{
    // Prepends the tool name to a log message (e.g. "ffmpeg: Downloading") so cumulative
    // multi-tool install logs read unambiguously. A null/blank name leaves the message as-is.
    internal static string Prefix(string? toolName, string message) =>
        string.IsNullOrWhiteSpace(toolName) ? message : $"{toolName}: {message}";
};

/// <summary>Per-install choices. Variant is honoured only by tools that have variants.</summary>
public sealed record InstallOptions(GpuVariant? Variant = null);

public sealed class ToolInstaller(
    IProcessRunner runner,
    AppPaths paths,
    BundledFetcher bundledFetcher,
    PlatformInfo platform
)
{
    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;
    private readonly BundledFetcher _bundledFetcher = bundledFetcher;
    private readonly PlatformInfo _platform = platform;

    /// <summary>
    /// Install a tool according to its catalog <see cref="InstallStrategy"/>. Dispatches to the
    /// script runner, uv-tool install, or bundled fetch.
    /// </summary>
    public Task InstallAsync(
        Tool tool,
        InstallOptions options,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default
    ) =>
        tool.InstallStrategy switch
        {
            ScriptInstall s => RunScriptAsync(s, progress, ct),
            UvToolInstall u => RunUvToolAsync(u, options.Variant, progress, ct),
            BundledFetch => _bundledFetcher.FetchAsync(tool, progress, ct),
            var other => throw new NotSupportedException(
                $"No installer for strategy {other.GetType().Name}."
            ),
        };

    private Task RunScriptAsync(
        ScriptInstall strategy,
        IProgress<InstallProgress>? progress,
        CancellationToken ct
    )
    {
        if (!strategy.Commands.TryGetValue(_platform.Os, out var command))
            throw new PlatformNotSupportedException($"No install script for {_platform.Os}.");

        return _runner.RunStreamingAsync(
            command.Executable,
            command.Arguments,
            AsLineProgress(progress),
            ct
        );
    }

    private Task RunUvToolAsync(
        UvToolInstall strategy,
        GpuVariant? variant,
        IProgress<InstallProgress>? progress,
        CancellationToken ct
    )
    {
        var selected = variant is { } v
            ? strategy.VariantsFor(_platform.Os).FirstOrDefault(x => x.Variant == v)
            : null;

        List<string> args = ["tool", "install"];
        if (strategy.PythonVersion is { } python)
            args.AddRange(["--python", python]);
        args.Add("--force");
        args.Add(selected is null ? strategy.Package : $"{strategy.Package}[{selected.PipExtra}]");
        if (selected is not null)
            args.AddRange(selected.ExtraArgs);

        return _runner.RunStreamingAsync(_paths.Uv, args, AsLineProgress(progress), ct);
    }

    public Task UninstallAsync(
        Tool tool,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        if (tool.InstallStrategy is not UvToolInstall u)
            throw new NotSupportedException($"{tool.CliName} cannot be uninstalled via uv.");

        return _runner.RunStreamingAsync(
            _paths.Uv,
            ["tool", "uninstall", u.Package],
            AsLineProgress(progress),
            ct
        );
    }

    /// <summary>
    /// Probe which <see cref="GpuVariant"/> is actually functional in the installed
    /// audio-separator environment, by running the catalog's variant probe script against the
    /// uv-managed Python. Returns null when the tool or its variants can't be determined.
    /// </summary>
    public async Task<GpuVariant?> DetectInstalledVariantAsync()
    {
        try
        {
            if (
                ToolCatalog.Get(ToolKind.AudioSeparator).InstallStrategy
                is not UvToolInstall { VariantProbe: { } probe }
            )
                return null;

            var toolDir = (await _runner.RunAsync(_paths.Uv, ["tool", "dir"])).Stdout;
            if (string.IsNullOrWhiteSpace(toolDir))
                return null;

            var pythonExe = new[]
            {
                Path.Combine(toolDir, "audio-separator", "Scripts", "python.exe"), // Windows
                Path.Combine(toolDir, "audio-separator", "bin", "python"), // macOS/Linux
            }.FirstOrDefault(File.Exists);

            if (pythonExe is null)
                return null;

            var scriptPath = Path.Combine(AppContext.BaseDirectory, probe.ScriptRelativePath);
            var result = (await _runner.RunAsync(pythonExe, ["-u", scriptPath])).Stdout;

            return Enum.TryParse<GpuVariant>(result.Trim(), out var variant) ? variant : null;
        }
        catch
        {
            return null;
        }
    }

    // Adapts an InstallProgress sink to the IProgress<string> that streaming process runs report
    // to, wrapping each raw line as a message-only InstallProgress. Synchronous so log ordering
    // is preserved (unlike Progress<T>, which marshals through a SynchronizationContext).
    private static IProgress<string>? AsLineProgress(IProgress<InstallProgress>? progress) =>
        progress is null ? null : new LineProgress(progress);

    private sealed class LineProgress(IProgress<InstallProgress> inner) : IProgress<string>
    {
        public void Report(string value) => inner.Report(new InstallProgress(value));
    }
}

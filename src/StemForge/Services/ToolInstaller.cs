using StemForge.Models;

namespace StemForge.Services;

/// <summary>Progress update from an install: a phase/log message, optionally with byte counts.</summary>
public sealed record InstallProgress(
    string Message,
    long? BytesDownloaded = null,
    long? TotalBytes = null
);

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

    // ── Legacy per-tool entry points ─────────────────────────────────────────
    // Thin adapters over InstallAsync so the existing view-models keep building. Removed once
    // the wizard and settings view-models call InstallAsync directly (catalog migration B/C).

    public Task InstallUvAsync(IProgress<string> progress, CancellationToken ct = default) =>
        InstallAsync(ToolCatalog.Get(ToolKind.Uv), new(), AsInstallProgress(progress), ct);

    public Task InstallYtdlpAsync(IProgress<string> progress, CancellationToken ct = default) =>
        InstallAsync(ToolCatalog.Get(ToolKind.Ytdlp), new(), AsInstallProgress(progress), ct);

    public Task InstallAudioSeparatorAsync(
        GpuVariant variant,
        IProgress<string> progress,
        CancellationToken ct = default
    ) =>
        InstallAsync(
            ToolCatalog.Get(ToolKind.AudioSeparator),
            new(variant),
            AsInstallProgress(progress),
            ct
        );

    public Task UninstallAudioSeparatorAsync(
        IProgress<string> progress,
        CancellationToken ct = default
    ) => UninstallAsync(ToolCatalog.Get(ToolKind.AudioSeparator), AsInstallProgress(progress), ct);

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

    // Adapts a legacy string-line sink to an InstallProgress sink, forwarding the message only
    // (byte counts are dropped — the legacy callers render log lines, not progress bars).
    private static IProgress<InstallProgress>? AsInstallProgress(IProgress<string>? progress) =>
        progress is null ? null : new MessageProgress(progress);

    private sealed class MessageProgress(IProgress<string> inner) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => inner.Report(value.Message);
    }
}

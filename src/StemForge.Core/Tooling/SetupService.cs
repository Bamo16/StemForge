using System.Net.Http;

namespace StemForge.Core.Tooling;

/// <summary>What setup found: the GPUs, the variant it suggests and the variants this platform offers.</summary>
public sealed record SetupDetection(
    IReadOnlyList<DetectedGpu> Gpus,
    GpuVariant Suggested,
    IReadOnlyList<GpuVariant> Available
);

public enum SetupStepStatus
{
    Installing,
    Installed,
    Skipped,
    Failed,
}

/// <summary>One tool's turn in a setup run; <paramref name="Message"/> carries the failure reason.</summary>
public sealed record SetupStep(string Tool, SetupStepStatus Status, string? Message = null);

public sealed record SetupResult(GpuVariant Variant, IReadOnlyList<SetupStep> Steps)
{
    public bool Succeeded => Steps.All(step => step.Status is not SetupStepStatus.Failed);
}

/// <summary>
/// What the setup wizard does, without the window: detect the GPU, install every tool missing on
/// this platform, and record the variant in the settings (the caller saves them).
/// </summary>
public sealed class SetupService(
    SetupDetector detector,
    GpuDetector gpuDetector,
    ToolInstaller installer,
    AppSettings settings,
    PlatformInfo platform
)
{
    private readonly SetupDetector _detector = detector;
    private readonly GpuDetector _gpuDetector = gpuDetector;
    private readonly ToolInstaller _installer = installer;
    private readonly AppSettings _settings = settings;
    private readonly PlatformInfo _platform = platform;

    public async Task<SetupDetection> DetectAsync()
    {
        var gpus = await _gpuDetector.DetectAsync();
        var available = AvailableVariants();
        var suggested = GpuDetector.SuggestVariant(gpus);

        // A platform without the suggested build (AMD on Linux has no DirectML) falls back to CPU.
        return new(gpus, available.Contains(suggested) ? suggested : GpuVariant.Cpu, available);
    }

    public async Task<SetupResult> RunAsync(
        GpuVariant variant,
        IProgress<SetupStep>? steps = null,
        IProgress<InstallProgress>? log = null,
        CancellationToken ct = default
    )
    {
        if (!AvailableVariants().Contains(variant))
            throw new ArgumentException(
                $"audio-separator has no {variant} build on {_platform.Os}.",
                nameof(variant)
            );

        var states = await _detector.DetectAsync();
        List<SetupStep> done = [];

        foreach (var tool in ToolCatalog.AvailableFor(_platform))
        {
            ct.ThrowIfCancellationRequested();

            var found = states.Single(state => state.Kind == tool.Kind).Found;
            var installed =
                tool.Kind is ToolKind.AudioSeparator && found
                    ? await InstalledVariantAsync()
                    : null;

            // An installed audio-separator of another variant is reinstalled; one whose variant
            // cannot be told is left alone.
            var reinstall = installed is { } onDisk && onDisk != variant;

            if (found && !reinstall)
            {
                Add(new(tool.CliName, SetupStepStatus.Skipped));

                continue;
            }

            Add(new(tool.CliName, SetupStepStatus.Installing), record: false);
            Add(await InstallOneAsync(tool, variant, log, ct));
        }

        var result = new SetupResult(variant, done);
        _settings.GpuVariant = variant;
        if (result.Succeeded)
            _settings.FirstRunComplete = true;

        return result;

        void Add(SetupStep step, bool record = true)
        {
            if (record)
                done.Add(step);
            steps?.Report(step);
        }
    }

    private async Task<SetupStep> InstallOneAsync(
        Tool tool,
        GpuVariant variant,
        IProgress<InstallProgress>? log,
        CancellationToken ct
    )
    {
        try
        {
            await _installer.InstallAsync(
                tool,
                new InstallOptions(tool.Kind is ToolKind.AudioSeparator ? variant : null),
                log,
                ct
            );

            if (!(await _detector.DetectAsync(tool.Kind))[0].Found)
                return new(
                    tool.CliName,
                    SetupStepStatus.Failed,
                    "Installed but could not be found."
                );

            if (tool.Kind is ToolKind.AudioSeparator)
                _settings.InstalledVariant = variant;

            return new(tool.CliName, SetupStepStatus.Installed);
        }
        catch (Exception ex)
            when (ex
                    is ProcessRunner.ProcessFailedException
                        or IOException
                        or HttpRequestException
                        or PlatformNotSupportedException
                        or InvalidOperationException
            )
        {
            return new(tool.CliName, SetupStepStatus.Failed, ex.Message);
        }
    }

    // The variant on disk: what the last install recorded, else what the environment reports.
    private async Task<GpuVariant?> InstalledVariantAsync()
    {
        if (_settings.InstalledVariant is { } recorded)
            return recorded;

        var probed = await _installer.DetectInstalledVariantAsync();
        _settings.InstalledVariant = probed;

        return probed;
    }

    private List<GpuVariant> AvailableVariants() =>
        ToolCatalog.Get(ToolKind.AudioSeparator).InstallStrategy is UvToolInstall uv
            ? [.. uv.VariantsFor(_platform.Os).Select(v => v.Variant)]
            : [];
}

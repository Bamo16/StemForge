using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Json;
using StemForge.Core.Tooling;

namespace StemForge.Cli.Commands;

internal sealed class SetupCommand : AsyncCommand<SetupCommand.Settings>
{
    private const string Suggested = "suggested";

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--variant <VARIANT>")]
        public string? Variant { get; set; }

        [CommandOption("--detect-only")]
        public bool DetectOnly { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }

        public override ValidationResult Validate() =>
            Variant is null
            || Variant.Equals(Suggested, StringComparison.OrdinalIgnoreCase)
            || ParseVariant(Variant) is not null
                ? ValidationResult.Success()
                : ValidationResult.Error("--variant must be suggested, cpu, cuda or directml.");
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        var setup = provider.GetRequiredService<SetupService>();
        var detection = await setup.DetectAsync();

        if (settings.Json)
            CliJson.Write(DetectionLine(detection));
        else
            WriteDetection(detection);

        if (settings.DetectOnly)
            return 0;

        var chosen = ChooseVariant(settings, detection);
        if (chosen is not { } variant)
        {
            Console.Error.WriteLine(
                "Pass --variant suggested, cpu, cuda or directml: there is no terminal to ask on."
            );

            return 2;
        }

        if (!detection.Available.Contains(variant))
        {
            Console.Error.WriteLine(
                $"{variant} is not available on this platform ({string.Join(", ", detection.Available)})."
            );

            return 2;
        }

        var result = await setup.RunAsync(
            variant,
            new StepProgress(settings.Json),
            new StderrLog(),
            cancellationToken
        );
        await provider.GetRequiredService<AppSettings>().SaveAsync();

        if (settings.Json)
            CliJson.Write(
                new
                {
                    type = "summary",
                    variant = result.Variant.ToString(),
                    succeeded = result.Succeeded,
                    tools = result.Steps.Select(StepLine),
                }
            );
        else
            Console.WriteLine(
                result.Succeeded ? "Setup complete." : "Setup finished with failures."
            );

        return result.Succeeded ? 0 : 1;
    }

    private static GpuVariant? ChooseVariant(Settings settings, SetupDetection detection) =>
        settings.Variant switch
        {
            null when settings.Json || Console.IsInputRedirected => null,
            null => AnsiConsole.Prompt(
                new SelectionPrompt<GpuVariant>()
                    .Title("Which audio-separator build should be installed?")
                    .AddChoices(detection.Available.OrderBy(v => v == detection.Suggested ? 0 : 1))
                    .UseConverter(v => v == detection.Suggested ? $"{v} (suggested)" : v.ToString())
            ),
            var value when value.Equals(Suggested, StringComparison.OrdinalIgnoreCase) =>
                detection.Suggested,
            var value => ParseVariant(value),
        };

    private static GpuVariant? ParseVariant(string value) =>
        value.ToLowerInvariant() switch
        {
            "cpu" => GpuVariant.Cpu,
            "cuda" => GpuVariant.Cuda,
            "directml" => GpuVariant.DirectML,
            _ => null,
        };

    private static void WriteDetection(SetupDetection detection)
    {
        Console.WriteLine(
            detection.Gpus.Count is 0
                ? "No GPU detected."
                : $"Detected: {string.Join(", ", detection.Gpus.Select(gpu => gpu.Name))}"
        );
        Console.WriteLine($"Suggested variant: {detection.Suggested}");
    }

    private static object DetectionLine(SetupDetection detection) =>
        new
        {
            type = "detection",
            gpus = detection.Gpus.Select(gpu => new
            {
                name = gpu.Name,
                vendor = gpu.Vendor.ToString(),
            }),
            suggested = detection.Suggested.ToString(),
            variants = detection.Available.Select(v => v.ToString()),
        };

    private static object StepLine(SetupStep step) =>
        new
        {
            tool = step.Tool,
            status = step.Status.ToString().ToLowerInvariant(),
            message = step.Message,
        };

    // One JSON line per step on stdout, or a plain line; synchronous, unlike Progress<T>, so the
    // order is kept and nothing is still queued when the command returns.
    private sealed class StepProgress(bool json) : IProgress<SetupStep>
    {
        public void Report(SetupStep value)
        {
            if (json)
                CliJson.Write(
                    new
                    {
                        type = "tool",
                        tool = value.Tool,
                        status = value.Status.ToString().ToLowerInvariant(),
                        message = value.Message,
                    }
                );
            else
                Console.WriteLine(
                    value.Message is { } message
                        ? $"{value.Tool}: {value.Status} ({message})"
                        : $"{value.Tool}: {value.Status}"
                );
        }
    }

    // Standard error, so --json keeps stdout to the JSON lines.
    private sealed class StderrLog : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value)
        {
            if (value.BytesDownloaded is null)
                Console.Error.WriteLine(value.Message);
        }
    }
}

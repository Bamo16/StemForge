using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Json;

namespace StemForge.Cli.Commands;

internal sealed class PresetsCommand : AsyncCommand<PresetsCommand.Settings>
{
    private sealed record PresetResult(string Id, string? Algorithm, IReadOnlyList<string> Models);

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--json")]
        public bool Json { get; set; }
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

        var catalog = provider.GetRequiredService<PresetCatalogService>();

        IReadOnlyList<Preset> presets;
        try
        {
            presets = await catalog.ListPresetsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (settings.Json)
                Console.Error.WriteLine(ex.Message);
            else
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }

        if (presets.Count == 0)
        {
            if (settings.Json)
            {
                CliJson.Write(Array.Empty<PresetResult>());
                return 1;
            }

            AnsiConsole.MarkupLine(
                "[yellow]No presets found. Ensure the toolchain is installed (run the GUI setup first).[/]"
            );
            return 1;
        }

        if (settings.Json)
        {
            CliJson.Write(
                presets
                    .Select(p => new PresetResult(p.Id, p.EnsembleAlgorithm, p.AllModels))
                    .ToList()
            );
            return 0;
        }

        var presetTable = presets.Aggregate(
            new Table().AddColumns("ID", "Algorithm", "Models"),
            (table, preset) =>
                table.AddRow(
                    Markup.Escape(preset.Id),
                    Markup.Escape(preset.EnsembleAlgorithm ?? string.Empty),
                    Markup.Escape(string.Join(", ", preset.AllModels))
                )
        );

        AnsiConsole.Write(presetTable);
        return 0;
    }
}

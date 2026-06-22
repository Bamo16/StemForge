using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace StemForge.Cli.Commands;

internal sealed class PresetsCommand : AsyncCommand<PresetsCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

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
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }

        if (presets.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No presets found. Ensure the toolchain is installed (run the GUI setup first).[/]"
            );
            return 1;
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

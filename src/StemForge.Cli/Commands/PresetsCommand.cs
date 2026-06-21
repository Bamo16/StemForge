using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Core.Services;

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

        IReadOnlyList<Core.Models.Preset> presets;
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

        var table = new Table();
        table.AddColumn("ID");
        table.AddColumn("Algorithm");
        table.AddColumn("Models");

        foreach (var preset in presets)
        {
            var modelList = string.Join(", ", preset.AllModels);
            table.AddRow(
                Markup.Escape(preset.Id),
                Markup.Escape(preset.EnsembleAlgorithm ?? ""),
                Markup.Escape(modelList)
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

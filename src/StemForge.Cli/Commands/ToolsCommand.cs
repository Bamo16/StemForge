using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using StemForge.Cli.Json;
using StemForge.Core.Tooling;

namespace StemForge.Cli.Commands;

internal sealed class ToolsCommand : AsyncCommand<ToolsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--json")]
        public bool Json { get; set; }

        [CommandOption("--ensure")]
        public bool Ensure { get; set; }
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

        var service = provider.GetRequiredService<ToolReportService>();

        IReadOnlyList<ToolReport> tools;
        try
        {
            tools = await service.ReportAsync(
                settings.Ensure,
                new StderrProgress(),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }

        if (settings.Json)
        {
            CliJson.Write(tools);

            return 0;
        }

        AnsiConsole.Write(
            tools.Aggregate(
                new Table().AddColumns("Tool", "Path", "Installed", "Overridden", "Version"),
                (table, tool) =>
                    table.AddRow(
                        Markup.Escape(tool.Kind),
                        Markup.Escape(tool.Path),
                        tool.Installed ? "yes" : "no",
                        tool.Overridden ? "yes" : "no",
                        Markup.Escape(tool.Version ?? string.Empty)
                    )
            )
        );

        return 0;
    }

    // Standard error, so --json keeps stdout to the one payload. Synchronous, unlike Progress<T>,
    // so nothing is still queued when the command returns.
    private sealed class StderrProgress : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value)
        {
            if (value.BytesDownloaded is null)
                Console.Error.WriteLine(value.Message);
        }
    }
}

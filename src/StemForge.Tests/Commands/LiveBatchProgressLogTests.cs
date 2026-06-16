using Spectre.Console;
using StemForge.Cli.Progress;
using StemForge.Core.Services;

namespace StemForge.Tests.Commands;

public sealed class LiveBatchProgressLogTests
{
    private static IAnsiConsole NonTerminalConsole(TextWriter sink) =>
        AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(sink),
            }
        );

    // The level tag and the source/message all flow through Spectre markup parsing. A bare [WRN]
    // or a bracketed message would be read as a (nonexistent) style and throw; these must not.
    [Theory]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    public void Log_does_not_throw_on_level_tags_or_bracketed_text(LogLevel level)
    {
        var sink = new StringWriter();
        var progress = new LiveBatchProgress(NonTerminalConsole(sink), verbose: true);

        var ex = Record.Exception(() =>
            progress.Log(level, "driver", "value [in brackets] and a [WRN] token")
        );

        Assert.Null(ex);
    }
}

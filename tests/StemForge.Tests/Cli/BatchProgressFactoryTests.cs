using Spectre.Console;
using StemForge.Cli.Progress;

namespace StemForge.Tests.Cli;

/// <summary>
/// Tests that verbose forces the plain (non-live) renderer (#51). The live Spectre display is
/// driven from the main thread while high-rate driver logs are written from a background thread;
/// those writes tear the live region into ghost bar fragments. Verbose sidesteps it by using the
/// plain renderer, which has no live region to corrupt.
/// </summary>
public sealed class BatchProgressFactoryTests
{
    // A console whose profile reports an interactive, ANSI-capable terminal so that, absent the
    // verbose gate, the factory would otherwise be eligible for the live renderer.
    private static IAnsiConsole InteractiveConsole()
    {
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                Out = new AnsiConsoleOutput(new StringWriter()),
            }
        );
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Capabilities.Ansi = true;
        return console;
    }

    [Fact]
    public void Create_Verbose_AlwaysReturnsPlainRenderer()
    {
        var display = BatchProgressFactory.Create(InteractiveConsole(), verbose: true);

        Assert.IsType<PlainBatchProgress>(display);
    }

    [Fact]
    public void ShouldUseLiveDisplay_NonInteractiveProfile_IsFalse()
    {
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings { Out = new AnsiConsoleOutput(new StringWriter()) }
        );
        console.Profile.Capabilities.Interactive = false;

        Assert.False(BatchProgressFactory.ShouldUseLiveDisplay(console));
    }
}

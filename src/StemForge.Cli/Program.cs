using Spectre.Console.Cli;
using StemForge.Cli.Commands;
using StemForge.Cli.Progress;
using StemForge.Core.Services;

AppLogger.Initialize();

// Route log entries to the active progress display when one is running; otherwise fall back to
// writing warnings and errors to standard error.
ProgressLogBridge.RegisterSink();

var app = new CommandApp();
app.Configure(config =>
{
    config
        .AddCommand<PresetsCommand>("presets")
        .WithDescription("List built-in separation presets from the audio-separator toolchain.");
    config
        .AddCommand<SeparateCommand>("separate")
        .WithDescription("Separate a local audio file into stems using a built-in preset.");
    config
        .AddCommand<DownloadCommand>("download")
        .WithDescription("Download audio from one or more URLs without separating.");
});

return await app.RunAsync(args);

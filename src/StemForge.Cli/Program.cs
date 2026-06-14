using Spectre.Console.Cli;
using StemForge.Cli.Commands;
using StemForge.Core.Services;

AppLogger.Initialize();
AppLogger.RegisterSink(entry =>
{
    if (entry is { Level: LogLevel.Warning or LogLevel.Error })
        Console.Error.WriteLine($"[{entry.LevelTag}] {entry.Source}: {entry.Message}");
});

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

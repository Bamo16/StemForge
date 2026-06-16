using System.Text;
using Spectre.Console.Cli;
using StemForge.Cli.Commands;
using StemForge.Cli.Progress;
using StemForge.Core.Services;

// Emit UTF-8 to the console so Unicode in log and progress output (the process arrows, box-drawing
// bars, track titles) renders correctly on Windows, where the default console codepage otherwise
// garbles the multi-byte sequences. No BOM so redirected output stays clean; best-effort because
// setting the encoding throws when no real console is attached.
try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch { }

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

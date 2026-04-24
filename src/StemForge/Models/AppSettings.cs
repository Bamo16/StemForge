namespace StemForge.Models;

public sealed class AppSettings
{
    public GpuVariant GpuVariant { get; set; } = GpuVariant.Cpu;
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;
    public string ModelsDirectory { get; set; } = DefaultModelsDirectory;
    public string? YtdlpPath { get; set; }
    public string? FfmpegPath { get; set; }
    public bool FirstRunComplete { get; set; } = false;

    public static string DefaultOutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "Stems");

    public static string DefaultModelsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "audio-separator",
            "models"
        );
}

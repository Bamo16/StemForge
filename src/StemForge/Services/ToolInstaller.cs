using System.Runtime.InteropServices;
using StemForge.Models;

namespace StemForge.Services;

public static class ToolInstaller
{
    public static async Task<bool> IsUvAvailableAsync()
    {
        try
        {
            return (await ProcessRunner.RunAsync("uv", ["--version"])).Success;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<GpuVariant?> DetectInstalledVariantAsync()
    {
        try
        {
            var toolDir = (await ProcessRunner.RunAsync("uv", ["tool", "dir"])).Stdout;
            if (string.IsNullOrWhiteSpace(toolDir))
                return null;

            var pythonExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(toolDir, "audio-separator", "Scripts", "python.exe")
                : Path.Combine(toolDir, "audio-separator", "bin", "python");

            if (!File.Exists(pythonExe))
                return null;

            const string script =
                "from importlib.metadata import distributions;"
                + "n={d.metadata['Name'].lower() for d in distributions()};"
                + "print('Cuda' if 'onnxruntime-gpu' in n else ('DirectML' if 'onnxruntime-directml' in n else 'Cpu'))";

            var result = (await ProcessRunner.RunAsync(pythonExe, ["-c", script])).Stdout;
            return result switch
            {
                "Cuda" => GpuVariant.Cuda,
                "DirectML" => GpuVariant.DirectML,
                "Cpu" => GpuVariant.Cpu,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    public static Task InstallAudioSeparatorAsync(
        GpuVariant variant,
        IProgress<string> progress,
        CancellationToken ct = default
    ) =>
        ProcessRunner.RunStreamingAsync(
            "uv",
            [
                "tool",
                "install",
                $"audio-separator[{SetupDetector.GetPipExtra(variant)}]",
                "--python",
                "3.10",
            ],
            progress,
            ct
        );
}

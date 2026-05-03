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

            var pythonExe = new[]
            {
                Path.Combine(toolDir, "audio-separator", "Scripts", "python.exe"), // Windows
                Path.Combine(toolDir, "audio-separator", "bin", "python"), // macOS/Linux
            }.FirstOrDefault(File.Exists);

            if (pythonExe is null)
                return null;

            // Check functional GPU support rather than just which packages are installed.
            // torch may not be present for cpu/dml installs, so guard the import.
            const string script = """
                try:
                    import torch; torch_gpu=torch.cuda.is_available()
                except ImportError:
                    torch_gpu=False
                import onnxruntime as ort; providers=ort.get_available_providers()
                print('Cuda' if torch_gpu and 'CUDAExecutionProvider' in providers else ('DirectML' if 'DMLExecutionProvider' in providers else 'Cpu'))
                """;

            var result = (await ProcessRunner.RunAsync(pythonExe, ["-c", script])).Stdout;

            return Enum.TryParse<GpuVariant>(result, out var variant) ? variant : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task InstallAudioSeparatorAsync(
        GpuVariant variant,
        IProgress<string> progress,
        CancellationToken ct = default
    )
    {
        var extra = SetupDetector.GetPipExtra(variant);
        List<string> args =
        [
            "tool",
            "install",
            "--python",
            "3.10",
            "--force",
            $"audio-separator[{extra}]",
        ];

        // PyTorch CUDA wheels are not on PyPI; cu121 requires CUDA 12.1+ drivers.
        if (variant == GpuVariant.Cuda)
            args.AddRange(["--extra-index-url", "https://download.pytorch.org/whl/cu121"]);

        await ProcessRunner.RunStreamingAsync("uv", [.. args], progress, ct);
    }
}

using System.Runtime.InteropServices;
using StemForge.Models;

namespace StemForge.Services;

public sealed class ToolInstaller(IProcessRunner runner)
{
    private readonly IProcessRunner _runner = runner;

    public async Task InstallUvAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await _runner.RunStreamingAsync(
                "powershell",
                ["-ExecutionPolicy", "ByPass", "-c", "irm https://astral.sh/uv/install.ps1 | iex"],
                progress,
                ct
            );
        }
        else
        {
            await _runner.RunStreamingAsync(
                "sh",
                ["-c", "curl -LsSf https://astral.sh/uv/install.sh | sh"],
                progress,
                ct
            );
        }
    }

    public async Task<bool> IsUvAvailableAsync()
    {
        try
        {
            return (await _runner.RunAsync("uv", ["--version"])).Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsYtdlpAvailableAsync()
    {
        try
        {
            return (await _runner.RunAsync("yt-dlp", ["--version"])).Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsFfmpegAvailableAsync()
    {
        try
        {
            return (await _runner.RunAsync("ffmpeg", ["-version"])).Success;
        }
        catch
        {
            return false;
        }
    }

    public Task InstallYtdlpAsync(
        IProgress<string> progress,
        CancellationToken ct = default
    ) => _runner.RunStreamingAsync("uv", ["tool", "install", "yt-dlp"], progress, ct);

    public Task InstallFfmpegAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return _runner.RunStreamingAsync(
                "winget",
                [
                    "install",
                    "--id",
                    "Gyan.FFmpeg",
                    "-e",
                    "--accept-source-agreements",
                    "--accept-package-agreements",
                ],
                progress,
                ct
            );

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return _runner.RunStreamingAsync("brew", ["install", "ffmpeg"], progress, ct);

        throw new PlatformNotSupportedException(
            "Automatic ffmpeg install is not supported on this platform. Install ffmpeg via your package manager."
        );
    }

    public async Task<GpuVariant?> DetectInstalledVariantAsync()
    {
        try
        {
            var toolDir = (await _runner.RunAsync("uv", ["tool", "dir"])).Stdout;
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

            var result = (await _runner.RunAsync(pythonExe, ["-c", script])).Stdout;

            return Enum.TryParse<GpuVariant>(result, out var variant) ? variant : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task InstallAudioSeparatorAsync(
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

        await _runner.RunStreamingAsync("uv", [.. args], progress, ct);
    }
}

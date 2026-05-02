using System.Runtime.InteropServices;
using StemForge.Models;

namespace StemForge.Services;

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
    Unknown,
}

public sealed record DetectedGpu(string Name)
{
    public GpuVendor Vendor { get; } =
        Name switch
        {
            _ when Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) => GpuVendor.Nvidia,

            _ when Name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                    || Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) => GpuVendor.Amd,

            _ when Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) => GpuVendor.Intel,

            _ => GpuVendor.Unknown,
        };
}

public static class GpuDetector
{
    public static async Task<IReadOnlyList<DetectedGpu>> DetectAsync() =>
        true switch
        {
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => await QueryWindowsAsync(),
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => await QueryLinuxAsync(),
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => await QueryMacAsync(),
            _ => [],
        };

    /// <summary>Returns the best GpuVariant for the detected hardware.</summary>
    public static GpuVariant SuggestVariant(IReadOnlyList<DetectedGpu> gpus) =>
        gpus switch
        {
            _ when gpus.Any(g => g is { Vendor: GpuVendor.Nvidia }) => GpuVariant.Cuda,

            _ when gpus.Any(g => g is { Vendor: GpuVendor.Amd or GpuVendor.Intel }) =>
                GpuVariant.DirectML,

            _ => GpuVariant.Cpu,
        };

    // Windows: Using the modern CIM call via PowerShell
    private static async Task<IReadOnlyList<DetectedGpu>> QueryWindowsAsync() =>
        ParseLines(
            await GetCommandOutputAsync(
                "powershell",
                [
                    "-NoProfile",
                    "-Command",
                    "Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name",
                ]
            )
        );

    // Linux: Direct shell execution
    private static async Task<IReadOnlyList<DetectedGpu>> QueryLinuxAsync() =>
        ParseLines(await GetCommandOutputAsync("sh", ["-c", "lspci -mm | grep -Ei 'VGA|Display'"]));

    // --- macOS: Using 'system_profiler' ---
    private static async Task<IReadOnlyList<DetectedGpu>> QueryMacAsync() =>
        // Look for lines containing "Chipset Model"
        [
            .. (await GetCommandOutputAsync("system_profiler", ["SPDisplaysDataType"]))
                .Split('\n')
                .Where(l => l.Contains("Chipset Model:"))
                .Select(l => l.Split(':')[1].Trim())
                .Select(name => new DetectedGpu(name)),
        ];

    private static async Task<string> GetCommandOutputAsync(string cmd, IEnumerable<string> args)
    {
        try
        {
            return (await ProcessRunner.RunAsync(cmd, args)).Stdout;
        }
        catch { return string.Empty; }
    }

    private static List<DetectedGpu> ParseLines(string raw, bool skipFirst = false) =>
        [
            .. raw.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Skip(skipFirst ? 1 : 0)
                .Select(name => new DetectedGpu(name)),
        ];
}

using System.Diagnostics;

namespace StemForge.Core.Extensions;

public static class ProcessStartInfoExtensions
{
    /// <summary>
    /// Adds or updates a single environment variable entry.
    /// </summary>
    public static ProcessStartInfo WithEnvironmentVariable(
        this ProcessStartInfo startInfo,
        string key,
        string? value
    ) => startInfo.WithEnvironmentVariables([(key, value)]);

    /// <summary>
    /// Adds or updates multiple environment variables using zero-allocation spans.
    /// </summary>
    public static ProcessStartInfo WithEnvironmentVariables(
        this ProcessStartInfo startInfo,
        params ReadOnlySpan<(string Key, string? Value)> variables
    )
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        foreach (var (key, value) in variables)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }
}

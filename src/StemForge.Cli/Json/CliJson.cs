using System.Text.Json;

namespace StemForge.Cli.Json;

/// <summary>
/// Shared serialization for every command's <c>--json</c> output, so automation callers get one
/// consistent naming convention (camelCase) across <c>download</c>, <c>separate</c>, and
/// <c>presets</c>, and <c>tools</c>.
/// </summary>
internal static class CliJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serializes <paramref name="value"/> and writes it to stdout as the sole payload.</summary>
    internal static void Write(object value) => Console.WriteLine(Serialize(value));

    internal static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}

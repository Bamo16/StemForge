using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Core.Tooling;

/// <summary>Reads the latest release from PyPI's JSON API.</summary>
public sealed class PyPiPackageIndex(IHttpClientFactory factory) : IPackageIndex
{
    public async Task<string?> LatestVersionAsync(string package, CancellationToken ct = default)
    {
        try
        {
            await using var stream = await factory
                .CreateClient("pypi")
                .GetStreamAsync($"https://pypi.org/pypi/{package}/json", ct);
            var project = await JsonSerializer.DeserializeAsync(
                stream,
                PyPiJsonContext.Default.PyPiProject,
                ct
            );
            return project?.Info.Version;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Debug("pypi", $"Latest {package} version unavailable: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Looks up the newest release of a Python package.</summary>
public interface IPackageIndex
{
    /// <summary>The latest version of <paramref name="package"/>, or null when it cannot be read.</summary>
    Task<string?> LatestVersionAsync(string package, CancellationToken ct = default);
}

public sealed record PyPiProject
{
    public PyPiProjectInfo Info { get; init; } = new();
}

public sealed record PyPiProjectInfo
{
    public string? Version { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PyPiProject))]
internal sealed partial class PyPiJsonContext : JsonSerializerContext { }

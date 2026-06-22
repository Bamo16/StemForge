using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Tests.Services;

/// <summary>
/// Pins the C# driver event/phase vocabulary to the shared manifest (tools/driver_protocol.json),
/// the single source of truth both the Python driver and this app derive from. Drift between the
/// manifest and the typed events fails the build here, without spawning Python. See ADR 0008.
/// </summary>
public sealed class DriverProtocolContractTests
{
    private static (HashSet<string> Events, HashSet<string> Phases) LoadManifest()
    {
        var path = AppPaths.DriverProtocolManifest;
        Assert.True(File.Exists(path), $"Driver protocol manifest not found at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var events = doc
            .RootElement.GetProperty("events")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToHashSet();
        var phases = doc
            .RootElement.GetProperty("phases")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToHashSet();
        return (events, phases);
    }

    // The polymorphic discriminators declared on the DriverEvent base via [JsonDerivedType].
    private static HashSet<string> CSharpEventDiscriminators() =>
        typeof(DriverEvent)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => (string)a.TypeDiscriminator!)
            .ToHashSet();

    // The wire names declared on the DriverPhase enum members via [JsonStringEnumMemberName].
    private static HashSet<string> CSharpPhaseNames() =>
        typeof(DriverPhase)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f =>
                f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                ?? throw new InvalidOperationException(
                    $"DriverPhase.{f.Name} is missing [JsonStringEnumMemberName]"
                )
            )
            .ToHashSet();

    [Fact]
    public void EventDiscriminators_MatchManifest()
    {
        var (manifest, _) = LoadManifest();
        AssertSetsEqual("event", manifest, CSharpEventDiscriminators());
    }

    [Fact]
    public void PhaseNames_MatchManifest()
    {
        var (_, manifest) = LoadManifest();
        AssertSetsEqual("phase", manifest, CSharpPhaseNames());
    }

    private static void AssertSetsEqual(
        string kind,
        HashSet<string> manifest,
        HashSet<string> csharp
    )
    {
        var missingInCSharp = manifest.Except(csharp).Order().ToList();
        var extraInCSharp = csharp.Except(manifest).Order().ToList();
        Assert.True(
            missingInCSharp.Count == 0 && extraInCSharp.Count == 0,
            $"Driver {kind} vocabulary drifted from tools/driver_protocol.json. "
                + $"In manifest but not C#: [{string.Join(", ", missingInCSharp)}]; "
                + $"in C# but not manifest: [{string.Join(", ", extraInCSharp)}]."
        );
    }
}

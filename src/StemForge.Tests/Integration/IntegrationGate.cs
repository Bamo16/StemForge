namespace StemForge.Tests.Integration;

/// <summary>
/// Shared gate for the slow, environment-dependent integration tests. These are SKIPPED by
/// default so the normal <c>dotnet test</c> suite stays fast and self-contained; they run only
/// when <c>STEMFORGE_INTEGRATION=1</c> is set. The separation test additionally requires a fully
/// provisioned toolchain (separator driver, models) and the download test reaches the network, so
/// neither belongs in the default run.
///
/// Run them on demand with:
/// <code>STEMFORGE_INTEGRATION=1 dotnet test src/StemForge.Tests/StemForge.Tests.csproj</code>
/// or filter to just this namespace:
/// <code>STEMFORGE_INTEGRATION=1 dotnet test --filter "FullyQualifiedName~Integration"</code>
/// </summary>
internal static class IntegrationGate
{
    internal const string EnvGate = "STEMFORGE_INTEGRATION";

    internal const string SkipReason = "Set STEMFORGE_INTEGRATION=1 to run integration tests.";

    /// <summary>Referenced by <c>SkipUnless</c>: true only when the integration env gate is set.</summary>
    public static bool Enabled =>
        Environment.GetEnvironmentVariable(EnvGate) is "1" or "true" or "TRUE";
}

using System.Reflection;

namespace StemForge.Services;

/// <summary>
/// Single source for the application's identity (product name and version). Centralizes the
/// version lookup so call sites depend on an injected abstraction rather than reflecting over the
/// executing assembly directly. This is also the value surfaced to the UI.
/// </summary>
public interface IAppInfo
{
    /// <summary>Product name, e.g. "StemForge".</summary>
    string ProductName { get; }

    /// <summary>
    /// Three-part version, e.g. "0.1.1". Suitable for an HTTP user-agent product token.
    /// </summary>
    string ShortVersion { get; }

    /// <summary>Full version as resolved from the assembly, e.g. "0.1.1.0".</summary>
    string FullVersion { get; }
}

/// <summary>
/// Default <see cref="IAppInfo"/> backed by the executing assembly's version. Reflection happens
/// once at construction (in <see cref="Current"/>) rather than at each call site, so consumers stay
/// unit-testable via a plain constructed instance.
/// </summary>
public sealed class AppInfo(string productName, Version? version) : IAppInfo
{
    private const string Fallback = "dev";
    public string ProductName { get; } = productName;
    public string ShortVersion => version?.ToString(3) ?? Fallback;
    public string FullVersion => version?.ToString() ?? Fallback;

    /// <summary>Builds an <see cref="AppInfo"/> from the executing assembly's metadata.</summary>
    public static AppInfo Current { get; } =
        new("StemForge", Assembly.GetExecutingAssembly().GetName().Version);
}

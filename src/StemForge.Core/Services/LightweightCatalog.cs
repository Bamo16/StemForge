namespace StemForge.Core.Services;

/// <summary>
/// Shared plumbing for the torch-free catalog listing one-shots (list_models.py, list_presets.py):
/// runs the script via the audio-separator interpreter and carves the JSON object out of stdout.
/// Each catalog service keeps its own DTO parsing and caching; only the run + stdout-cleanup steps,
/// which were duplicated across them, live here.
/// </summary>
internal static class LightweightCatalog
{
    /// <summary>
    /// Runs <paramref name="script"/> (plus <paramref name="args"/>) via the audio-separator Python
    /// interpreter and returns its stdout. Returns an empty string when the script is missing or the
    /// process fails; diagnostics are logged under <paramref name="logSource"/>.
    /// </summary>
    public static async Task<string> RunScriptAsync(
        IProcessRunner runner,
        AppPaths paths,
        string script,
        IReadOnlyList<string> args,
        string logSource,
        CancellationToken ct
    )
    {
        var scriptName = Path.GetFileName(script);

        if (!File.Exists(script))
        {
            AppLogger.Warning(logSource, $"{scriptName} not found at: {script}");
            return "";
        }

        ProcessRunner.Result result;
        try
        {
            result = await runner.RunAsync(
                paths.SeparationDriverPython,
                [script, .. args],
                ct,
                logRawLines: false
            );
        }
        catch (Exception ex)
        {
            AppLogger.Warning(logSource, $"Failed to run {scriptName}: {ex.Message}");
            return "";
        }

        // JSON goes to stdout; fall back to combined output if stdout is empty.
        return string.IsNullOrWhiteSpace(result.Stdout) ? result.Output : result.Stdout;
    }

    /// <summary>
    /// Carves the outermost JSON object out of <paramref name="raw"/>, tolerating stray log lines
    /// the scripts or their imports may print before or after it. Returns null when no object is
    /// present.
    /// </summary>
    public static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start < 0 || end <= start ? null : raw[start..(end + 1)];
    }
}

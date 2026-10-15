using System.ComponentModel;
using System.Globalization;

namespace StemForge.Core.Tooling;

/// <summary>
/// Keeps the bundled yt-dlp current by running its own <c>--update</c> at most once a day, since
/// YouTube stops serving an old yt-dlp within months (#95). A user's override is left alone.
/// </summary>
public sealed class YtdlpUpdater(IProcessRunner runner, AppPaths paths, TimeProvider time)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly IProcessRunner _runner = runner;
    private readonly AppPaths _paths = paths;
    private readonly TimeProvider _time = time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Beside the binary, so the record of a check goes wherever the binary goes.
    private string CheckedMarker => $"{_paths.BundledYtdlp}.update-checked";

    /// <summary>Updates the bundled yt-dlp unless it was checked within the last day; true when it ran.</summary>
    public async Task<bool> EnsureCurrentAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsDue())
                return false;

            // --update stays on the installed channel, so a user who moved to nightly stays there.
            var result = await _runner
                .RunAsync(_paths.BundledYtdlp, ["--update"], ct: ct)
                .ConfigureAwait(false);
            AppLogger.Info("yt-dlp", result.Output.Trim());

            await File.WriteAllTextAsync(
                    CheckedMarker,
                    _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
                    ct
                )
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
            when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // The binary couldn't run or the marker couldn't be written; the installed copy still
            // works, so carry on with it. A failed update itself exits without throwing.
            AppLogger.Warning("yt-dlp", $"Update check failed: {ex.Message}");

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsDue() =>
        _paths.Ytdlp == _paths.BundledYtdlp
        && File.Exists(_paths.BundledYtdlp)
        && (
            !TryReadLastCheck(out var checkedAt)
            || _time.GetUtcNow() - checkedAt >= CheckInterval
            // A binary written after the last check was reinstalled at the pinned version.
            || File.GetLastWriteTimeUtc(_paths.BundledYtdlp)
                > File.GetLastWriteTimeUtc(CheckedMarker)
        );

    private bool TryReadLastCheck(out DateTimeOffset checkedAt)
    {
        checkedAt = default;

        return File.Exists(CheckedMarker)
            && DateTimeOffset.TryParse(
                File.ReadAllText(CheckedMarker),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out checkedAt
            );
    }
}

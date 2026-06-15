using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Cli.Progress;

/// <summary>
/// Resolves a URL input to its metadata up front, mirroring the GUI's resolve-then-process flow.
/// The pipeline can resolve a URL itself, but doing it here first lets a command announce the
/// resolved title (the eventual filename) as the input label instead of the raw URL, and surface a
/// resolution failure (bad URL or network) as a failed input before any progress bar is drawn.
///
/// The resolved metadata is handed back so the command can pass it as
/// <see cref="JobRecord.PreResolvedMeta"/>; the pipeline reuses it rather than resolving a second
/// time.
/// </summary>
internal static class UrlInputResolver
{
    /// <summary>
    /// Outcome of resolving one URL: either the metadata plus the display title to label the input
    /// with, or a failure reason to mark the input failed with.
    /// </summary>
    internal sealed record Outcome
    {
        private Outcome() { }

        public YtDlpMetadata? Meta { get; private init; }
        public string? Title { get; private init; }
        public string? FailureReason { get; private init; }

        public bool Succeeded => Meta is not null;

        public static Outcome Resolved(YtDlpMetadata meta) =>
            new() { Meta = meta, Title = meta.DisplayTitle };

        public static Outcome Failed(string reason) => new() { FailureReason = reason };
    }

    /// <summary>
    /// Resolves <paramref name="url"/> to metadata. Returns a <see cref="Outcome"/> describing the
    /// resolved title and metadata, or the failure reason. A cancellation is rethrown so the caller
    /// can stop the batch; any other resolution error becomes a failed outcome.
    /// </summary>
    public static async Task<Outcome> ResolveAsync(
        YouTubeAudioService youTubeAudio,
        string url,
        AppSettings settings,
        CancellationToken ct
    )
    {
        try
        {
            var meta = await youTubeAudio.ResolveAsync(url, settings, log: null, ct);
            return Outcome.Resolved(meta);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Outcome.Failed(ex.Message);
        }
    }
}

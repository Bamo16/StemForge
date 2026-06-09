using StemForge.Core.Models;

namespace StemForge.Cli.Progress;

/// <summary>
/// Maps a <see cref="JobUpdate"/> to a short human-readable activity string for the
/// per-input progress line (e.g. "Downloading", "Loading model 1/2", "Separating Vocals").
/// </summary>
internal static class PhaseActivity
{
    /// <summary>
    /// Returns a concise current-activity description for the given update, or null when the
    /// update carries no display-worthy activity (e.g. a bare log line).
    /// </summary>
    internal static string? Describe(JobUpdate update)
    {
        switch (update.Phase)
        {
            case "downloading":
                return "Downloading source";

            case "starting":
                return update.RunLabel is { Length: > 0 } label ? $"Starting {label}" : "Starting";

            case "loading_model":
                if (update.ModelIndex is { } mi && update.ModelCount is { } mc && mc > 1)
                    return update.Cached == true
                        ? $"Loading model {mi}/{mc} (cached)"
                        : $"Loading model {mi}/{mc}";
                return update.Cached == true ? "Loading model (cached)" : "Loading model";

            case "downloading_model":
                return update.Model is { Length: > 0 } model
                    ? $"Fetching model {model}"
                    : "Fetching model";

            case "progress":
                return update.RunLabel is { Length: > 0 } runLabel
                    ? $"Separating {runLabel}"
                    : "Separating";

            case "ensembling":
                return update.Stem is { Length: > 0 } stem
                    ? $"Combining {stem}"
                    : "Combining stems";

            case "stem_written":
                return "Writing stems";

            case "tagging":
                return "Tagging output";

            case "keep_source":
                return "Keeping source";

            case "run_complete":
                return update.RunLabel is { Length: > 0 } completeLabel
                    ? $"Finished {completeLabel}"
                    : "Finished";

            default:
                return null;
        }
    }
}

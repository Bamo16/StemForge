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
    /// update carries no display-worthy activity (e.g. a bare log line). The model arguments are
    /// the caller's running memory of the active model: the driver names the model on
    /// loading_model but not on every progress tick, so the separating text would otherwise be
    /// identical for every model in a multi-model preset.
    /// </summary>
    internal static string? Describe(
        JobUpdate update,
        string? model = null,
        int? modelIndex = null,
        int? modelCount = null
    )
    {
        var idx = update.ModelIndex ?? modelIndex;
        var count = update.ModelCount ?? modelCount;
        var name = (update.Model ?? model) is { Length: > 0 } m ? m : null;

        switch (update.Phase)
        {
            case "downloading":
                return "Downloading source";

            case "starting":
                return update.RunLabel is { Length: > 0 } label ? $"Starting {label}" : "Starting";

            case "loading_model":
            {
                var cached = update.Cached == true ? " (cached)" : "";
                if (idx is { } i && count is { } c && c > 1)
                {
                    var named = name is not null ? $": {name}" : "";
                    return $"Loading model {i}/{c}{named}{cached}";
                }
                return $"Loading model{cached}";
            }

            case "downloading_model":
                return name is not null ? $"Fetching model {name}" : "Fetching model";

            case "progress":
            {
                var separating = update.RunLabel is { Length: > 0 } runLabel
                    ? $"Separating {runLabel}"
                    : "Separating";
                if (idx is { } i && count is { } c && c > 1)
                {
                    var named = name is not null ? $": {name}" : "";
                    return $"{separating} (model {i}/{c}{named})";
                }
                return separating;
            }

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

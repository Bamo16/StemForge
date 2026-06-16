using StemForge.Core.Models;

namespace StemForge.Cli.Progress;

/// <summary>
/// An <see cref="IProgress{T}"/> of <see cref="JobUpdate"/> for a single input that feeds an
/// <see cref="IInputProgress"/>. It does two things the raw pipeline updates do not:
///
/// 1. Keeps the reported percentage monotonic. The pipeline drives one overall percentage for the
///    whole job, but it re-emits a run's starting percentage on non-progress phases (loading a
///    model, combining stems). Fed straight to a single bar those would drag it backwards, which is
///    what made the old display look like it kept resetting between models. We report the running
///    peak instead so the one bar only ever moves forward.
///
/// 2. Remembers the active model. The driver reports the model name and index on loading_model but
///    not on every progress tick, so the separating text is given that memory to name the model
///    currently running (model 2/2: Kim_Vocal_2) rather than an identical "Separating" for each.
///
/// Updates are handled synchronously (unlike <see cref="Progress{T}"/>, which would post them to
/// the thread pool in a console app, racing this mutable state and reordering ticks). A lock guards
/// the state in case the pipeline reports from more than one thread.
/// </summary>
internal sealed class JobProgressReporter(IInputProgress input) : IProgress<JobUpdate>
{
    private readonly Lock _gate = new();
    private string? _model;
    private int? _modelIndex;
    private int? _modelCount;
    private int _peak;

    public static IProgress<JobUpdate> For(IInputProgress input) => new JobProgressReporter(input);

    public void Report(JobUpdate update)
    {
        lock (_gate)
        {
            if (update.Model is { Length: > 0 } m)
                _model = m;
            if (update.ModelIndex is { } i)
                _modelIndex = i;
            if (update.ModelCount is { } c)
                _modelCount = c;

            if (update.OverallPercent > _peak)
                _peak = update.OverallPercent;

            input.Report(_peak, PhaseActivity.Describe(update, _model, _modelIndex, _modelCount));
        }
    }
}

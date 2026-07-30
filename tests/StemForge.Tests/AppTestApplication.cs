using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(StemForge.Tests.AppTestApplication))]
// Avalonia headless uses a single shared Application/Dispatcher. xUnit's default
// collection parallelization races on it under load and intermittently drops an
// [AvaloniaFact] test from the run (it is not collected rather than failed), so the
// reported test count wobbles. Serialize collections to keep the run deterministic.
// The suite runs in a few seconds, so the lost parallelism is not a meaningful cost.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace StemForge.Tests;

public sealed class AppTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<AppTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

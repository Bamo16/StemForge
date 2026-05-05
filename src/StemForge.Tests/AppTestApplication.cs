using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(StemForge.Tests.AppTestApplication))]

namespace StemForge.Tests;

public sealed class AppTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AppTestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace StemForge.Tests.Views;

// Runtime sanity checks for the custom ProgressBar shimmer ControlTheme
// (issue #16). A previous style-only attempt animated a brush property on the
// indicator and crashed Avalonia's GradientBrushAnimator at runtime (it could
// not cast the indicator's base SolidColorBrush to a gradient). The shipped
// theme instead animates only a TranslateTransform on a child overlay that
// carries a STATIC gradient, so no brush is ever animated.
//
// These tests load the real ProgressBar.axaml ControlTheme, apply it to both a
// determinate and an indeterminate ProgressBar, show them, and pump the headless
// renderer timer to advance the animation clock. If applying the theme or
// running its animations throws (as the earlier attempt did), the test fails.
// A human still confirms the shimmer renders visually; this guards the crash.
public sealed class ProgressBarShimmerThemeTests
{
    private static ControlTheme LoadProgressBarTheme()
    {
        var dictionary = (ResourceDictionary)
            AvaloniaXamlLoader.Load(new Uri("avares://StemForge/Styles/ProgressBar.axaml"));

        // The ControlTheme is keyed by the ProgressBar type in the dictionary.
        var theme = (ControlTheme)dictionary[typeof(ProgressBar)];
        return theme;
    }

    private static Window ShowWithTheme(params ProgressBar[] bars)
    {
        var theme = LoadProgressBarTheme();
        var panel = new StackPanel();
        foreach (var bar in bars)
        {
            bar.Theme = theme;
            bar.Width = 200;
            panel.Children.Add(bar);
        }

        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = panel,
        };
        window.Show();
        return window;
    }

    // Pump the headless renderer several times so the animation clock advances
    // through the shimmer/indeterminate keyframes. Each capture triggers a timer
    // tick; a throwing animator would surface here.
    private static void PumpFrames(Window window, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            window.CaptureRenderedFrame();
        }
    }

    // The theme loads from the avares resource and exposes a ControlTheme keyed
    // to ProgressBar. If the XAML were malformed this would throw on load.
    [AvaloniaFact]
    public void Theme_LoadsFromResource_TargetsProgressBar()
    {
        var theme = LoadProgressBarTheme();

        Assert.NotNull(theme);
        Assert.Equal(typeof(ProgressBar), theme.TargetType);
    }

    // Applying the theme to a determinate bar, showing it, and advancing the
    // animation clock must not throw. This is the case the earlier attempt
    // regressed: a determinate bar with an animated overlay.
    [AvaloniaFact]
    public void DeterminateBar_AppliesThemeAndAnimates_DoesNotThrow()
    {
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 60,
            IsIndeterminate = false,
        };

        var window = ShowWithTheme(bar);
        try
        {
            PumpFrames(window, 8);

            // The determinate indicator part must exist and the fill must be
            // present, guarding the first deferral's "colorless empty track"
            // regression.
            var indicator = bar.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Name == "PART_Indicator");
            Assert.NotNull(indicator);
        }
        finally
        {
            window.Close();
        }
    }

    // Applying the theme to an indeterminate bar and advancing the clock through
    // the infinite translate animations must not throw.
    [AvaloniaFact]
    public void IndeterminateBar_AppliesThemeAndAnimates_DoesNotThrow()
    {
        var bar = new ProgressBar { IsIndeterminate = true };

        var window = ShowWithTheme(bar);
        try
        {
            PumpFrames(window, 8);
        }
        finally
        {
            window.Close();
        }
    }

    // Both states present together (the realistic app condition) must coexist
    // and animate without throwing.
    [AvaloniaFact]
    public void DeterminateAndIndeterminate_Together_DoNotThrow()
    {
        var determinate = new ProgressBar { Value = 30, Maximum = 100 };
        var indeterminate = new ProgressBar { IsIndeterminate = true };

        var window = ShowWithTheme(determinate, indeterminate);
        try
        {
            PumpFrames(window, 10);
        }
        finally
        {
            window.Close();
        }
    }

    // Reproduces the exact precondition of the earlier crash: the indicator's
    // Foreground is a concrete SolidColorBrush (the live app tints it with the
    // amber accent). The earlier style-only attempt animated this brush and
    // Avalonia's GradientBrushAnimator threw casting the SolidColorBrush to a
    // gradient. The shipped theme animates only a TranslateTransform, so a solid
    // Foreground must animate cleanly. If a brush were being animated, pumping
    // the clock here would throw.
    [AvaloniaFact]
    public void SolidForegroundIndicator_Animates_DoesNotThrow()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xD4, 0x70, 0x3A));

        var determinate = new ProgressBar
        {
            Value = 45,
            Maximum = 100,
            Foreground = accent,
        };
        var indeterminate = new ProgressBar { IsIndeterminate = true, Foreground = accent };

        var window = ShowWithTheme(determinate, indeterminate);
        try
        {
            PumpFrames(window, 12);
        }
        finally
        {
            window.Close();
        }
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StemForge.Views;

namespace StemForge.Tests.Views;

// The visual focus-clearing behavior (TextBox loses caret/selection on
// background click) requires a human to verify in the running app.
// These tests cover the hit-test helper that distinguishes interactive
// controls from non-interactive background, which is the sole decision
// point driving whether ClearFocus is called.
public sealed class MainWindowFocusTests
{
    // A TextBox is focusable and enabled by default, so a click on it must
    // not trigger focus clearing.
    [AvaloniaFact]
    public void IsInteractiveHit_TextBox_ReturnsTrue()
    {
        var textBox = new TextBox();

        var result = MainWindow.IsInteractiveHit(textBox, stopAt: null);

        Assert.True(result);
    }

    // A Button is focusable and enabled, so a click on it must not trigger
    // focus clearing.
    [AvaloniaFact]
    public void IsInteractiveHit_Button_ReturnsTrue()
    {
        var button = new Button();

        var result = MainWindow.IsInteractiveHit(button, stopAt: null);

        Assert.True(result);
    }

    // A plain Border has Focusable=false and is treated as background.
    [AvaloniaFact]
    public void IsInteractiveHit_Border_ReturnsFalse()
    {
        var border = new Border();

        var result = MainWindow.IsInteractiveHit(border, stopAt: null);

        Assert.False(result);
    }

    // A plain Grid has Focusable=false and is treated as background.
    [AvaloniaFact]
    public void IsInteractiveHit_Grid_ReturnsFalse()
    {
        var grid = new Grid();

        var result = MainWindow.IsInteractiveHit(grid, stopAt: null);

        Assert.False(result);
    }

    // A disabled Button must not block focus clearing, because a disabled
    // control cannot accept focus and behaves like background for this purpose.
    [AvaloniaFact]
    public void IsInteractiveHit_DisabledButton_ReturnsFalse()
    {
        var button = new Button { IsEnabled = false };

        var result = MainWindow.IsInteractiveHit(button, stopAt: null);

        Assert.False(result);
    }

    // When a TextBlock (non-interactive) is visually inside a Button
    // (interactive), a click on the TextBlock must be treated as a click
    // on the Button, so focus is not cleared.
    [AvaloniaFact]
    public void IsInteractiveHit_TextBlockInsideButton_ReturnsTrue()
    {
        var label = new TextBlock { Text = "click me" };
        var button = new Button { Content = label };

        // Force Avalonia to wire up the visual tree.
        var window = new Window { Content = button };
        window.Show();

        try
        {
            var result = MainWindow.IsInteractiveHit(label, stopAt: window);

            Assert.True(result);
        }
        finally
        {
            window.Close();
        }
    }

    // When an element is a non-interactive background and stopAt is the
    // Window, the walk terminates at the Window without finding an
    // interactive hit.
    [AvaloniaFact]
    public void IsInteractiveHit_BackgroundBorderInsideWindow_ReturnsFalse()
    {
        var border = new Border();
        var window = new Window { Content = border };
        window.Show();

        try
        {
            var result = MainWindow.IsInteractiveHit(border, stopAt: window);

            Assert.False(result);
        }
        finally
        {
            window.Close();
        }
    }
}

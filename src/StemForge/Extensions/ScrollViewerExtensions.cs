using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace StemForge.Extensions;

/// <summary>
/// Attached property: when set to true on a ScrollViewer, the view auto-scrolls to
/// the bottom whenever its content grows — but only if the user was already near
/// the bottom, so manual scroll-up to copy/paste is preserved.
/// </summary>
public static class ScrollViewerExtensions
{
    private const double StickyThresholdPx = 80;

    public static readonly AttachedProperty<bool> AutoScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "AutoScrollToEnd",
            typeof(ScrollViewerExtensions)
        );

    public static bool GetAutoScrollToEnd(ScrollViewer sv) => sv.GetValue(AutoScrollToEndProperty);

    public static void SetAutoScrollToEnd(ScrollViewer sv, bool value) =>
        sv.SetValue(AutoScrollToEndProperty, value);

    static ScrollViewerExtensions()
    {
        AutoScrollToEndProperty.Changed.AddClassHandler<ScrollViewer>(OnAutoScrollChanged);
    }

    private static void OnAutoScrollChanged(ScrollViewer sv, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            sv.PropertyChanged += OnScrollViewerPropertyChanged;
        else
            sv.PropertyChanged -= OnScrollViewerPropertyChanged;
    }

    private static void OnScrollViewerPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e
    )
    {
        if (e.Property != ScrollViewer.ExtentProperty || sender is not ScrollViewer sv)
            return;

        // When extent goes from zero, the pane just became visible with existing content — jump to end.
        if (e.OldValue is Size { Height: 0 } && sv.Extent.Height > 0)
        {
            Dispatcher.UIThread.Post(sv.ScrollToEnd, DispatcherPriority.Background);
            return;
        }

        var distanceFromBottom = sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height;
        if (distanceFromBottom < StickyThresholdPx)
            Dispatcher.UIThread.Post(sv.ScrollToEnd, DispatcherPriority.Background);
    }
}

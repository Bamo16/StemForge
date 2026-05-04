using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class LogsView : UserControl
{
    private ScrollViewer? _scroll;

    public LogsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _scroll = this.FindControl<ScrollViewer>("LogScroll");
        if (DataContext is LogsViewModel vm)
        {
            vm.Displayed.CollectionChanged += OnDisplayedChanged;
            ScrollToEnd();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (DataContext is LogsViewModel vm)
            vm.Displayed.CollectionChanged -= OnDisplayedChanged;
    }

    private void OnDisplayedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Auto-scroll only if the user is already near the bottom.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_scroll is null)
                    return;
                var distanceFromBottom =
                    _scroll.Extent.Height - _scroll.Offset.Y - _scroll.Viewport.Height;
                if (distanceFromBottom < 80)
                    ScrollToEnd();
            },
            DispatcherPriority.Background
        );
    }

    private void ScrollToEnd() =>
        Dispatcher.UIThread.Post(() => _scroll?.ScrollToEnd(), DispatcherPriority.Background);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class MainWindow : Window
{
    private LogsWindow? _logsWindow;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(
            PointerPressedEvent,
            OnWindowPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: false
        );
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is MainWindowViewModel vm)
            vm.ShowLogsRequested += OnShowLogsRequested;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (DataContext is MainWindowViewModel vm)
            vm.ShowLogsRequested -= OnShowLogsRequested;
        _logsWindow?.Close();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;
        if (IsInteractiveHit(source, stopAt: this))
            return;

        FocusManager?.Focus(null!);
    }

    // Walks the visual tree from <paramref name="source"/> up to (but not
    // including) <paramref name="stopAt"/>.  Returns true when any element
    // in the chain is a focusable, enabled InputElement, which means the
    // click is on an interactive control and focus should not be cleared.
    // Non-interactive containers (Panel, Grid, Border, ContentControl, etc.)
    // have Focusable=false by default and are treated as background.
    internal static bool IsInteractiveHit(Visual source, Visual? stopAt)
    {
        Visual? cur = source;
        while (cur is not null && cur != stopAt)
        {
            if (cur is InputElement { Focusable: true, IsEnabled: true })
                return true;
            cur = cur.GetVisualParent();
        }
        return false;
    }

    private void OnShowLogsRequested()
    {
        if (_logsWindow is { IsVisible: true })
        {
            _logsWindow.Activate();
            return;
        }

        var vm = ((MainWindowViewModel)DataContext!).Logs;
        _logsWindow = new LogsWindow { DataContext = vm };
        _logsWindow.Show();
    }
}

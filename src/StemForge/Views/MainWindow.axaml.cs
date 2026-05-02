using Avalonia.Controls;
using Avalonia.Interactivity;
using StemForge.ViewModels;

namespace StemForge.Views;

public partial class MainWindow : Window
{
    private LogsWindow? _logsWindow;

    public MainWindow()
    {
        InitializeComponent();
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

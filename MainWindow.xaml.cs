using System.Windows;
using System.Windows.Input;
using Bluzic.Services;
using Bluzic.ViewModels;

namespace Bluzic;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        var settingsService = new SettingsService();
        var bluetoothService = new BluetoothSinkService(settingsService);
        var volumeService = new AudioVolumeService();

        _viewModel = new MainViewModel(bluetoothService, volumeService, settingsService);
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--minimized") || args.Contains("-minimized"))
        {
            Hide();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            MyTaskbarIcon.ShowBalloonTip(
                "Bluzic", 
                "Приложение свёрнуто в трей и продолжает принимать аудио в фоновом режиме.", 
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TaskbarIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitExit = true;
        MyTaskbarIcon.Dispose();
        Application.Current.Shutdown();
    }
}
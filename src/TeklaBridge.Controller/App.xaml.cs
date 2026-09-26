using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SvMcp.Bridge;

namespace TeklaBridge.Controller;

public partial class App : Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "svMCP TeklaBridge Controller";
    private const string PerformanceLogPath = @"C:\temp\svmcp-perf.log";

    private BridgeControllerService? _service;
    private BridgePipeServer? _pipeServer;
    private TaskbarIcon? _icon;
    private MenuItem? _statusItem;
    private MenuItem? _startItem;
    private MenuItem? _stopItem;
    private MenuItem? _restartItem;
    private MenuItem? _autoStartItem;
    private bool _settingAutoStart;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\svMcpTeklaBridgeController", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _service = new BridgeControllerService();
        _pipeServer = new BridgePipeServer(_service);
        _pipeServer.Start();
        _service.StatusChanged += OnStatusChanged;

        var contextMenu = CreateContextMenu();
        _icon = new TaskbarIcon
        {
            IconSource = CreateTrayImage(),
            ToolTipText = "svMCP TeklaBridge",
            ContextMenu = contextMenu,
            Visibility = Visibility.Visible
        };
        RefreshStatus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_service != null)
            _service.StatusChanged -= OnStatusChanged;
        _icon?.Dispose();
        _pipeServer?.Dispose();
        _service?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private ContextMenu CreateContextMenu()
    {
        _statusItem = new MenuItem { Header = "svMCP: starting", IsEnabled = false };
        _startItem = new MenuItem { Header = "Start / Resume", StaysOpenOnClick = true };
        _stopItem = new MenuItem { Header = "Stop Bridge", StaysOpenOnClick = true };
        _restartItem = new MenuItem { Header = "Restart Bridge", StaysOpenOnClick = true };
        _autoStartItem = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = IsAutoStartEnabled(), StaysOpenOnClick = true };

        _startItem.Click += (_, _) => RunAction(() => _service!.Resume(), "Bridge started");
        _stopItem.Click += (_, _) => RunAction(() => _service!.Stop(), "Bridge stopped");
        _restartItem.Click += (_, _) => RunAction(() => _service!.Restart(), "Bridge restarted");
        _autoStartItem.Checked += AutoStartChanged;
        _autoStartItem.Unchecked += AutoStartChanged;

        var menu = new ContextMenu { StaysOpen = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_autoStartItem);
        var openLogItem = new MenuItem { Header = "Open log", StaysOpenOnClick = true };
        openLogItem.Click += (_, _) => OpenLog();
        var exitItem = new MenuItem { Header = "Exit", StaysOpenOnClick = true };
        exitItem.Click += (_, _) => ExitController();
        menu.Items.Add(openLogItem);
        menu.Items.Add(exitItem);
        menu.Opened += (_, _) => RefreshStatus();
        return menu;
    }

    private void RefreshStatus()
    {
        if (_service == null || _icon == null)
            return;

        var status = _service.GetStatus();
        var connection = status.TeklaConnection switch
        {
            "Connected" => "Tekla: last check connected",
            "Disconnected" => "Tekla: last check disconnected",
            _ => "Tekla: not checked"
        };
        var text = $"Bridge: {status.State}; {connection}";
        if (_statusItem != null)
            _statusItem.Header = text;
        _icon.ToolTipText = text;
        if (_startItem != null)
            _startItem.IsEnabled = status.Paused || !status.BridgeRunning;
        if (_stopItem != null)
            _stopItem.IsEnabled = !status.Paused;
        if (_restartItem != null)
            _restartItem.IsEnabled = !status.Paused || status.BridgeRunning;
    }

    private void RunAction(Action action, string successMessage)
    {
        Task.Run(() =>
        {
            try
            {
                action();
                Dispatcher.BeginInvoke(() => _icon?.ShowBalloonTip("svMCP", successMessage, BalloonIcon.Info));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => _icon?.ShowBalloonTip("svMCP", ex.Message, BalloonIcon.Error));
            }
            Dispatcher.BeginInvoke(RefreshStatus);
        });
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(RunValueName) is string;
    }

    private void AutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_settingAutoStart || _autoStartItem == null)
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (_autoStartItem.IsChecked)
            {
                var executable = Path.Combine(AppContext.BaseDirectory, "TeklaBridge.Controller.exe");
                key.SetValue(RunValueName, $"\"{executable}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not update startup setting", MessageBoxButton.OK, MessageBoxImage.Error);
            _settingAutoStart = true;
            _autoStartItem.IsChecked = IsAutoStartEnabled();
            _settingAutoStart = false;
        }
    }

    private void OpenLog()
    {
        if (File.Exists(PerformanceLogPath))
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{PerformanceLogPath}\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("explorer.exe", @"C:\temp") { UseShellExecute = true });
    }

    private void ExitController()
    {
        Task.Run(() =>
        {
            try
            {
                _service?.Stop();
                Dispatcher.BeginInvoke(new Action(Shutdown));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => MessageBox.Show(ex.Message, "Could not stop TeklaBridge", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        });
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshStatus);

    private static ImageSource CreateTrayImage()
    {
        var background = new GeometryDrawing(Brushes.SteelBlue, null, new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var bridge = Geometry.Parse("M3,4 L7,4 L7,2 L11,2 L11,4 L13,4 L13,12 L11,12 L11,14 L7,14 L7,12 L3,12 Z M7,6 L9,6 L9,10 L7,10 Z");
        var foreground = new GeometryDrawing(Brushes.White, null, bridge);
        return new DrawingImage(new DrawingGroup { Children = { background, foreground } });
    }
}

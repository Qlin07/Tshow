using System.Diagnostics;
using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Tshow.Data;
using Tshow.Services;
using Tshow.ViewModels;

namespace Tshow;

public partial class App : Application
{
    private const string AppMutexName = "Tshow_Instance_Mutex";
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("Tshow 已经在运行中。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Database init error: {ex.Message}");
        }

        var monitor = new ProcessMonitorService();
        monitor.OnError += (msg) => Debug.WriteLine(msg);

        var viewModel = new MainViewModel(monitor);
        _mainWindow = new MainWindow(viewModel);

        InitializeTrayIcon();

        _mainWindow.Show();
        _mainWindow.Loaded += (s, args) =>
        {
            viewModel.RefreshCurrentProcessesCommand.Execute(null);
        };
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Tshow - 软件使用时长监测",
            Icon = CreateDefaultIcon(),
            Visibility = Visibility.Visible
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "打开主窗口" };
        showItem.Click += (s, e) => ShowMainWindow();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (s, e) => ShutdownApplication();

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.TrayLeftMouseUp += (s, e) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ShutdownApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    private static System.Drawing.Icon CreateDefaultIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath);
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _mainWindow?.Close();
        _mainWindow = null;

        GC.KeepAlive(typeof(Mutex));

        base.OnExit(e);
    }
}

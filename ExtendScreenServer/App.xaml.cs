using System.Windows;

namespace ExtendScreenServer;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize system tray icon
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "ExtendScreen Server"
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Settings", null, (s, args) => ShowMainWindow());
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, args) => ShowMainWindow();

        // Create main window, show settings panel on first startup
        _mainWindow = new MainWindow();
        _mainWindow.Show();

        // Show balloon tip to inform the user the program is running in the tray
        _trayIcon.ShowBalloonTip(3000, "ExtendScreen",
            "The program is running in the system tray. Waiting for device connection...",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mainWindow?.ViewModel?.Shutdown();
        base.OnExit(e);
    }
}
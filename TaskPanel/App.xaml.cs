using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskPanel;

public partial class App : Application
{
    // Pinning to the taskbar/desktop means the exe can get launched again while
    // it's already running (e.g. clicking the pinned icon). Rather than spawn a
    // second, confusing instance, a second launch just signals the first one to
    // come to front and then exits immediately.
    private const string MutexName = "TaskPanel-9F1B3C7A-SingleInstance";
    private const string BringToFrontEventName = "TaskPanel-9F1B3C7A-BringToFront";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _bringToFrontEvent;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(BringToFrontEventName);
                existingEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The other instance is still starting up and hasn't created the
                // event yet; nothing we can signal, so just let this instance exit.
            }
            Shutdown();
            return;
        }

        _bringToFrontEvent = new EventWaitHandle(false, EventResetMode.AutoReset, BringToFrontEventName);
        StartBringToFrontListener();

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        SetupTrayIcon();
    }

    private void StartBringToFrontListener()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                _bringToFrontEvent!.WaitOne();
                Dispatcher.BeginInvoke(BringToFront, DispatcherPriority.Normal);
            }
        })
        { IsBackground = true };
        thread.Start();
    }

    private void SetupTrayIcon()
    {
        var icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "TaskPanel.exe");

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Bring to front", null, (_, _) => BringToFront());
        menu.Items.Add("Hide", null, (_, _) => HideWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Master Tasks",
            Visible = true,
            ContextMenuStrip = menu,
        };
        // The panel isn't kept always-on-top (so Chrome, Explorer, etc. can cover it
        // normally) — left-clicking the tray icon is the deliberate way to pull it back
        // up. Right-click is left alone so it only opens the context menu above.
        _trayIcon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) BringToFront(); };
        _trayIcon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) BringToFront(); };
        _trayIcon.BalloonTipClicked += (_, _) => BringToFront();
    }

    private void BringToFront() => _mainWindow?.BringToFront();

    /// <summary>Shows a small tray balloon — used for "this task is due soon" reminders.</summary>
    public void ShowReminder(string title, string text)
        => _trayIcon?.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None);

    private void HideWindow()
    {
        _mainWindow?.SaveNow();
        _mainWindow?.Hide();
    }

    public void ExitApp()
    {
        _mainWindow?.SaveNow();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _bringToFrontEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        Shutdown();
    }
}

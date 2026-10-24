using System.Windows;
using System.Windows.Threading;
using FacePresence.Localization;
using FacePresence.Presence;
using FacePresence.UI;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace FacePresence.App;

/// <summary>Tray icon, session lock handling and app lifetime. The main window is only hidden and shown.</summary>
public sealed class TrayHost
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private readonly MainWindow _window;
    private readonly WinForms.ToolStripMenuItem _openItem;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private readonly System.Threading.Timer _watchdog;
    private readonly RegisteredWaitHandle _showWait;
    private long _lastUiPongTicks = Environment.TickCount64;
    private bool _hangLogged;
    private bool _exiting;

    private Dispatcher Ui => _window.Dispatcher;

    public TrayHost(bool startMinimized, EventWaitHandle showSignal)
    {
        _host = new AppHost();
        _window = new MainWindow(_host) { PauseToggleRequested = TogglePause };

        var menu = new WinForms.ContextMenuStrip();
        _openItem = new WinForms.ToolStripMenuItem("", null, (_, _) => ShowWindow()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
        _pauseItem = new WinForms.ToolStripMenuItem("", null, (_, _) => TogglePause());
        _exitItem = new WinForms.ToolStripMenuItem("", null, (_, _) => ExitApp());
        menu.Items.Add(_openItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exitItem);
        ApplyLanguage();

        _icon = new WinForms.NotifyIcon
        {
            Text = L.T("app"),
            Icon = TrayIcons.Get(PresenceState.Starting),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowWindow();

        _host.Monitor.StatusChanged += OnStatus;
        L.Changed += ApplyLanguage;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Application.Current.SessionEnding += (_, _) => ExitApp();

        // A second start of the EXE only sends a signal; the window is shown here.
        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Ui.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        // Watchdog: logs when the UI thread stops responding (helps to diagnose "the window does not come up").
        _watchdog = new System.Threading.Timer(_ => CheckUi(), null, 5000, 5000);

        if (!startMinimized) ShowWindow();
        _ = _host.StartAsync();
    }

    private void CheckUi()
    {
        if (_exiting) return;
        var silence = Environment.TickCount64 - Interlocked.Read(ref _lastUiPongTicks);
        if (silence > 15000 && !_hangLogged)
        {
            _hangLogged = true;
            Log.Write($"WARNING: UI thread not responding for {silence / 1000} s");
        }
        Ui.BeginInvoke(() =>
        {
            if (_hangLogged)
            {
                Log.Write($"UI thread responsive again after {(Environment.TickCount64 - Interlocked.Read(ref _lastUiPongTicks)) / 1000} s");
                _hangLogged = false;
            }
            Interlocked.Exchange(ref _lastUiPongTicks, Environment.TickCount64);
        });
    }

    private void ApplyLanguage()
    {
        _openItem.Text = L.T("tray.open");
        _pauseItem.Text = L.T(_host.Monitor.IsPaused ? "btn.resume" : "btn.pause");
        _exitItem.Text = L.T("tray.exit");
    }

    private void OnStatus(StatusSnapshot s)
    {
        if (_exiting) return;
        Ui.BeginInvoke(() =>
        {
            if (_exiting) return;
            _icon.Icon = TrayIcons.Get(s.State);
            var text = L.T("app") + " – " + s.Message;
            _icon.Text = text.Length > 63 ? text[..63] : text;
        });
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Log.Write("Session locked/disconnected: " + e.Reason);
                _host.Monitor.SetLocked(true);
                _host.Keeper.Release();
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                Log.Write("Session unlocked/connected: " + e.Reason);
                _host.Monitor.SetLocked(false);
                break;
        }
    }

    private void ShowWindow() => _window.ShowAndActivate();

    public void TogglePause()
    {
        var paused = !_host.Monitor.IsPaused;
        _host.Monitor.SetPaused(paused);
        _pauseItem.Text = L.T(paused ? "btn.resume" : "btn.pause");
        _window.OnPauseChanged(paused);
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _watchdog.Dispose();
        _showWait.Unregister(null);
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _icon.Visible = false;
        _window.ForceClose();
        try { _host.Dispose(); } catch (Exception ex) { Log.Write("Dispose: " + ex.Message); }
        _icon.Dispose();
        Application.Current.Shutdown();
    }
}

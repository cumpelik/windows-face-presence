using FacePresence.Localization;
using FacePresence.Presence;
using FacePresence.UI;
using Microsoft.Win32;

namespace FacePresence.App;

/// <summary>The tray application; the main window is only hidden and shown.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly AppHost _host;
    private readonly NotifyIcon _icon;
    private readonly MainForm _form;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _exitItem;
    private bool _exiting;

    private readonly System.Threading.Timer _watchdog;
    private long _lastUiPongTicks = Environment.TickCount64;
    private bool _hangLogged;
    private RegisteredWaitHandle? _showWait;

    public TrayContext(bool startMinimized, EventWaitHandle showSignal)
    {
        _host = new AppHost();
        _form = new MainForm(_host) { PauseToggleRequested = TogglePause };
        _ = _form.Handle; // create the handle now so BeginInvoke works while the window is hidden

        var menu = new ContextMenuStrip();
        _openItem = new ToolStripMenuItem("", null, (_, _) => ShowForm()) { Font = new Font(menu.Font, FontStyle.Bold) };
        _pauseItem = new ToolStripMenuItem("", null, (_, _) => TogglePause());
        _exitItem = new ToolStripMenuItem("", null, (_, _) => ExitApp());
        menu.Items.Add(_openItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);
        ApplyLanguage();

        _icon = new NotifyIcon
        {
            Text = L.T("app"),
            Icon = TrayIcons.Get(PresenceState.Starting),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowForm();

        _host.Monitor.StatusChanged += OnStatus;
        L.Changed += ApplyLanguage;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += (_, _) => ExitApp();

        // A second start of the EXE only sends a signal; the window is shown here.
        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
        {
            try { _form.BeginInvoke(ShowForm); } catch { }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        // Watchdog: logs when the UI thread stops responding (helps to diagnose "the window does not come up").
        _watchdog = new System.Threading.Timer(_ => CheckUi(), null, 5000, 5000);

        if (!startMinimized) ShowForm();
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
        try
        {
            _form.BeginInvoke(() =>
            {
                if (_hangLogged)
                {
                    Log.Write($"UI thread responsive again after {(Environment.TickCount64 - Interlocked.Read(ref _lastUiPongTicks)) / 1000} s");
                    _hangLogged = false;
                }
                Interlocked.Exchange(ref _lastUiPongTicks, Environment.TickCount64);
            });
        }
        catch { /* window closed */ }
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
        try
        {
            _form.BeginInvoke(() =>
            {
                _icon.Icon = TrayIcons.Get(s.State);
                var text = L.T("app") + " – " + s.Message;
                _icon.Text = text.Length > 63 ? text[..63] : text;
            });
        }
        catch { /* window closed */ }
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

    private void ShowForm() => _form.ShowAndActivate();

    public void TogglePause()
    {
        var paused = !_host.Monitor.IsPaused;
        _host.Monitor.SetPaused(paused);
        _pauseItem.Text = L.T(paused ? "btn.resume" : "btn.pause");
        _form.OnPauseChanged(paused);
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _watchdog.Dispose();
        _showWait?.Unregister(null);
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _icon.Visible = false;
        _form.ForceClose();
        try { _host.Dispose(); } catch (Exception ex) { Log.Write("Dispose: " + ex.Message); }
        _icon.Dispose();
        ExitThread();
    }
}

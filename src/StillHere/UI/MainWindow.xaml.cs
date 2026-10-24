using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using StillHere.App;
using StillHere.Capture;
using StillHere.Localization;
using StillHere.Presence;
using StillHere.Recognition;
using StillHere.Settings;
using OpenCvSharp;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using CvPoint = OpenCvSharp.Point;
using WinRect = System.Windows.Rect;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace StillHere.UI;

public partial class MainWindow : FluentWindow
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _reallyClose;
    private bool _loading;
    private volatile bool _previewWanted;
    private CancellationTokenSource? _enrollCts;
    private StatusSnapshot _status;

    public Action? PauseToggleRequested { get; set; }

    public MainWindow(AppHost host)
    {
        _host = host;
        _status = host.Monitor.Status;
        _loading = true; // controls raise change events while the XAML is loaded
        InitializeComponent();

        Icon = TrayIcons.AppImage;
        TitleBar.Icon = new ImageIcon { Source = TrayIcons.AppImage };
        SourceInitialized += (_, _) => SystemThemeWatcher.Watch(this);

        PresentBox.ValueChanged += (_, _) => Apply(s => s.PresentIntervalSec = PresentBox.Value ?? s.PresentIntervalSec);
        AbsentBox.ValueChanged += (_, _) => Apply(s => s.AbsentIntervalSec = AbsentBox.Value ?? s.AbsentIntervalSec);

        LoadSettings();
        ApplyLanguage();
        L.Changed += ApplyLanguage;

        _host.Monitor.StatusChanged += s => Dispatcher.BeginInvoke(() => OnStatus(s));
        _host.Monitor.FrameAnalyzed += OnFrame;
        _host.CamerasChanged += () => Dispatcher.BeginInvoke(() => { FillCameras(); RenderEnrollment(); });
        IsVisibleChanged += (_, _) => _previewWanted = IsVisible;
        StateChanged += (_, _) => Log.Write("Window state -> " + WindowState);

        _tick.Tick += (_, _) => RenderStatus();
        _tick.Start();
    }

    // ---------- language ----------

    private void ApplyLanguage()
    {
        FlagEn.Source = TrayIcons.Flag("en");
        FlagCs.Source = TrayIcons.Flag("cs");
        FlagEn.Opacity = L.Lang == "en" ? 1 : 0.35;
        FlagCs.Opacity = L.Lang == "cs" ? 1 : 0.35;

        // footer: "Built together with {0} · Thanks to {1} for …" with both names as links
        var template = L.T("footer.text");
        var i0 = template.IndexOf("{0}", StringComparison.Ordinal);
        var i1 = template.IndexOf("{1}", StringComparison.Ordinal);
        FooterPart1.Text = template[..i0];
        FooterClaude.Text = L.T("footer.claude");
        FooterPart2.Text = template[(i0 + 3)..i1];
        FooterTabidoo.Text = L.T("footer.tabidoo");
        FooterPart3.Text = template[(i1 + 3)..];

        RenderPause(_host.Monitor.IsPaused);
        RenderEnrollment();
        if (_enrollCts == null) StateMessage.Text = _status.Message;
        RenderStatus();
    }

    private void SwitchLanguage(string lang)
    {
        if (L.Lang == lang) return;
        Apply(s => s.Language = lang);
        L.Set(lang);
    }

    private void FlagEn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => SwitchLanguage("en");
    private void FlagCs_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => SwitchLanguage("cs");

    // ---------- settings ----------

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _host.Settings;
            PresentBox.Value = s.PresentIntervalSec;
            AbsentBox.Value = s.AbsentIntervalSec;
            ThresholdSlider.Value = Math.Clamp(s.MatchThreshold, ThresholdSlider.Minimum, ThresholdSlider.Maximum);
            ThresholdText.Text = s.MatchThreshold.ToString("F2");
            try { AutostartSwitch.IsChecked = Autostart.IsEnabled(); } catch { }
            FillCameras();
        }
        finally { _loading = false; }
    }

    private void Apply(Action<AppSettings> change)
    {
        if (_loading) return;
        change(_host.Settings);
        _host.Settings.Save();
    }

    private void Threshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThresholdText == null) return; // raised during InitializeComponent
        var value = Math.Round(e.NewValue, 2);
        ThresholdText.Text = value.ToString("F2");
        Apply(s => s.MatchThreshold = value);
    }

    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { Autostart.Set(AutostartSwitch.IsChecked == true); }
        catch (Exception ex) { _ = ShowMessageAsync(L.T("dlg.autostart"), ex.Message); }
    }

    private void FillCameras()
    {
        var was = _loading;
        _loading = true;
        try
        {
            CameraCombo.ItemsSource = _host.Cameras.ToList();
            var current = _host.Monitor.Camera ?? _host.PreferredCamera();
            CameraCombo.SelectedItem = current == null ? null : _host.Cameras.FirstOrDefault(c => c.SourceId == current.SourceId);
        }
        finally { _loading = was; }
    }

    private async void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CameraCombo.SelectedItem is not CameraInfo cam) return;
        await _host.SelectCameraAsync(cam);
        RenderEnrollment();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _host.RefreshCamerasAsync();

    // ---------- enrollment ----------

    private void RenderEnrollment()
    {
        var all = _host.Settings.Templates;
        var kind = (_host.Monitor.Camera ?? _host.PreferredCamera())?.Kind ?? "IR";
        var mine = all.Where(t => t.CameraKind == kind).ToList();

        if (mine.Count > 0)
        {
            var when = mine.Max(t => t.Created).ToString(L.T("date.format"), CultureInfo.InvariantCulture);
            EnrollStatus.Text = L.T("enroll.status_ok", L.T("kind." + kind), when);
            EnrollIcon.Symbol = SymbolRegular.CheckmarkCircle24;
            EnrollIcon.Foreground = StateBrush(PresenceState.Present);
        }
        else
        {
            var others = all.Select(t => L.T("kind." + t.CameraKind)).Distinct().ToList();
            EnrollStatus.Text = others.Count > 0
                ? L.T("enroll.status_other", L.T("kind." + kind), string.Join(", ", others))
                : L.T("enroll.status_none");
            EnrollIcon.Symbol = SymbolRegular.ErrorCircle24;
            EnrollIcon.Foreground = StateBrush(PresenceState.Absent);
        }

        if (_enrollCts == null)
        {
            EnrollButton.Content = L.T(mine.Count > 0 ? "btn.reenroll" : "btn.enroll");
            EnrollButton.Appearance = mine.Count > 0 ? ControlAppearance.Secondary : ControlAppearance.Primary;
        }
        ClearButton.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Enroll_Click(object sender, RoutedEventArgs e)
    {
        if (_enrollCts != null)
        {
            _enrollCts.Cancel();
            return;
        }
        _enrollCts = new CancellationTokenSource();
        EnrollButton.Content = L.T("btn.enroll_cancel");
        EnrollButton.Appearance = ControlAppearance.Secondary;
        ClearButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(m => StateMessage.Text = L.T("enroll.progress", m));
            var result = await _host.Monitor.EnrollAsync(10, progress, _enrollCts.Token);
            await ShowMessageAsync(L.T("dlg.enroll"), result.Message);
        }
        finally
        {
            _enrollCts.Dispose();
            _enrollCts = null;
            ClearButton.IsEnabled = true;
            RenderEnrollment();
        }
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var box = new MessageBox
        {
            Owner = this,
            Title = L.T("app"),
            Content = new System.Windows.Controls.TextBlock { Text = L.T("dlg.clear_q"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = L.T("btn.forget"),
            CloseButtonText = L.T("btn.cancel"),
            MinWidth = 440,
        };
        if (await box.ShowDialogAsync() != MessageBoxResult.Primary) return;
        _host.Settings.Templates = new List<FaceTemplate>();
        _host.Settings.Save();
        RenderEnrollment();
    }

    // ---------- tools ----------

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var v = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
        var box = new MessageBox
        {
            Owner = this,
            Title = L.T("dlg.about"),
            Content = new System.Windows.Controls.TextBlock { Text = L.T("about.text", v, Log.Directory), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = L.T("btn.licenses"),
            CloseButtonText = L.T("btn.close"),
        };
        if (await box.ShowDialogAsync() == MessageBoxResult.Primary)
            new LicensesWindow { Owner = this }.Show();
    }

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", Log.Directory) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("Open data dir: " + ex.Message); }
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e) => new ProbeWindow(_host) { Owner = this }.Show();

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Open link: " + ex.Message); }
        e.Handled = true;
    }

    private Task<MessageBoxResult> ShowMessageAsync(string title, string text) =>
        new MessageBox
        {
            Owner = this,
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            CloseButtonText = L.T("btn.ok"),
        }.ShowDialogAsync();

    // ---------- pause ----------

    private void PauseButton_Click(object sender, RoutedEventArgs e) => PauseToggleRequested?.Invoke();

    public void OnPauseChanged(bool paused) => Dispatcher.BeginInvoke(() => RenderPause(paused));

    private void RenderPause(bool paused)
    {
        PauseButton.Content = L.T(paused ? "btn.resume" : "btn.pause");
        PauseIcon.Symbol = paused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
    }

    // ---------- status ----------

    private static readonly Dictionary<PresenceState, SolidColorBrush> Brushes = new();

    private static SolidColorBrush StateBrush(PresenceState state)
    {
        if (Brushes.TryGetValue(state, out var b)) return b;
        var color = state switch
        {
            PresenceState.Present => Color.FromRgb(22, 163, 74),
            PresenceState.Absent => Color.FromRgb(234, 88, 12),
            PresenceState.NotEnrolled or PresenceState.Enrolling => Color.FromRgb(37, 99, 235),
            PresenceState.CameraError => Color.FromRgb(220, 38, 38),
            _ => Color.FromRgb(107, 114, 128),
        };
        b = new SolidColorBrush(color);
        b.Freeze();
        Brushes[state] = b;
        return b;
    }

    private static SymbolRegular StateSymbolFor(PresenceState state) => state switch
    {
        PresenceState.Present => SymbolRegular.Checkmark24,
        PresenceState.Absent => SymbolRegular.Timer24,
        PresenceState.NotEnrolled or PresenceState.Enrolling => SymbolRegular.PersonAdd24,
        PresenceState.CameraError => SymbolRegular.CameraOff24,
        PresenceState.Locked => SymbolRegular.LockClosed24,
        PresenceState.Paused or PresenceState.Stopped => SymbolRegular.Pause24,
        _ => SymbolRegular.ArrowSync24,
    };

    private void OnStatus(StatusSnapshot s)
    {
        _status = s;
        StateCircle.Fill = StateBrush(s.State);
        StateSymbol.Symbol = StateSymbolFor(s.State);
        StateTitle.Foreground = StateBrush(s.State);
        if (_enrollCts == null) StateMessage.Text = s.Message;
        RenderStatus();
    }

    private void RenderStatus()
    {
        var s = _status;
        var idle = ActivityKeeper.GetIdleTime();
        var rule = LockPolicy.Get();

        StateTitle.Text = s.State switch
        {
            PresenceState.Present => L.T("state.present"),
            PresenceState.Absent => rule != null ? L.T("state.absent_in", L.Duration(rule.Timeout - idle)) : L.T("state.absent"),
            PresenceState.NotEnrolled => L.T("state.not_enrolled"),
            PresenceState.Enrolling => L.T("state.enrolling"),
            PresenceState.CameraError => L.T("state.camera_error"),
            PresenceState.Locked => L.T("state.locked"),
            PresenceState.Paused => L.T("state.paused"),
            PresenceState.Starting => L.T("state.starting"),
            _ => L.T("state.stopped"),
        };

        var none = L.T("stat.none");
        StatScore.Text = s.Score.HasValue ? $"{s.Score.Value:F2} / {_host.Settings.MatchThreshold:F2}" : none;
        StatIdle.Text = $"{idle.TotalSeconds:F0} s";
        var showNext = s.NextCheck.HasValue && s.State is PresenceState.Present or PresenceState.Absent or PresenceState.NotEnrolled;
        StatNext.Text = showNext ? $"{Math.Max(0, (s.NextCheck!.Value - DateTime.Now).TotalSeconds):F0} s" : none;
        StatLock.Text = rule != null ? $"{L.Duration(rule.Timeout)} · {rule.SourceName}" : L.T("stat.lock_unknown");
        StatInputs.Text = s.Injections > 0 ? L.T("stat.inputs_val", s.Injections, s.LastInjection?.ToString("HH:mm:ss") ?? "") : "0";
        StatCamera.Text = string.IsNullOrEmpty(s.CameraFormat) ? none : s.CameraFormat;
    }

    // ---------- preview ----------

    private void OnFrame(Mat frame, FaceResult? face, double? score)
    {
        if (!_previewWanted) return;
        try
        {
            using var canvas = frame.Clone();
            if (face != null)
            {
                var ok = score.HasValue && score.Value >= _host.Settings.MatchThreshold;
                var color = ok ? new Scalar(74, 163, 22) : new Scalar(12, 88, 234);
                DrawCornerBox(canvas, face.Box, color);
                var label = score.HasValue ? $"{score.Value:F2}" : $"det {face.Score:F2}";
                Cv2.PutText(canvas, label, new CvPoint(face.Box.X, Math.Max(14, face.Box.Y - 8)),
                    HersheyFonts.HersheySimplex, 0.55, color, 2, LineTypes.AntiAlias);
            }
            Cv2.ImEncode(".bmp", canvas, out var bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze(); // created on the monitor thread, used on the UI thread
            Dispatcher.BeginInvoke(() =>
            {
                PreviewBrush.ImageSource = image;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            Log.Write("Preview: " + ex.Message);
        }
    }

    /// <summary>Corner-only box – matches the app icon.</summary>
    private static void DrawCornerBox(Mat img, OpenCvSharp.Rect r, Scalar color)
    {
        var len = Math.Max(8, Math.Min(r.Width, r.Height) / 4);
        const int t = 2;
        void Line(int x1, int y1, int x2, int y2) => Cv2.Line(img, new CvPoint(x1, y1), new CvPoint(x2, y2), color, t, LineTypes.AntiAlias);
        int x0 = r.X, y0 = r.Y, x1 = r.X + r.Width, y1 = r.Y + r.Height;
        Line(x0, y0, x0 + len, y0); Line(x0, y0, x0, y0 + len);
        Line(x1, y0, x1 - len, y0); Line(x1, y0, x1, y0 + len);
        Line(x0, y1, x0 + len, y1); Line(x0, y1, x0, y1 - len);
        Line(x1, y1, x1 - len, y1); Line(x1, y1, x1, y1 - len);
    }

    // ---------- window ----------

    /// <summary>Shows the window in front; if it is outside the visible desktop (disconnected monitor), centers it on the primary screen.</summary>
    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        var desktop = new WinRect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!desktop.IntersectsWith(new WinRect(Left, Top, ActualWidth, ActualHeight)))
        {
            Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2;
            Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2;
            Log.Write("Window was off-screen, moved to the primary screen");
        }
        Activate();
        Topmost = true;  // reliably bring it in front of other windows …
        Topmost = false; // … without keeping it on top
        Focus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true; // closing only hides the window to the tray
            Hide();
            return;
        }
        _tick.Stop();
        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _reallyClose = true;
        Close();
    }
}

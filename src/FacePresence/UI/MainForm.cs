using FacePresence.App;
using FacePresence.Capture;
using FacePresence.Localization;
using FacePresence.Presence;
using FacePresence.Recognition;
using FacePresence.Settings;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;

namespace FacePresence.UI;

public sealed class MainForm : Form
{
    private readonly AppHost _host;
    private bool _reallyClose;
    private bool _loadingControls;
    private volatile bool _previewWanted;
    private CancellationTokenSource? _enrollCts;
    private PresenceState _headerState = PresenceState.Starting;

    public Action? PauseToggleRequested { get; set; }

    // header
    private readonly PictureBox _stateIcon = new();
    private readonly Label _lblState = new();
    private readonly Label _lblMessage = new();
    private readonly Label _statScore = new();
    private readonly Label _statIdle = new();
    private readonly Label _statNext = new();
    private readonly Label _statLock = new();
    private readonly Label _statInputs = new();
    private readonly Label _statCamera = new();

    // preview + camera
    private readonly PictureBox _preview = new();
    private readonly ComboBox _cbCamera = new();

    // settings
    private readonly NumericUpDown _nudPresent = Theme.Numeric(0.5m, 300m, 1, 1m);
    private readonly NumericUpDown _nudAbsent = Theme.Numeric(0.2m, 60m, 1, 0.5m);
    private readonly NumericUpDown _nudThreshold = Theme.Numeric(0.05m, 0.95m, 2, 0.01m);
    private readonly PictureBox _flagEn = new();
    private readonly PictureBox _flagCs = new();
    private readonly CheckBox _chkAutostart = new();
    private readonly Label _lblTemplates = new();
    private Button _btnEnroll = null!;
    private Button _btnClear = null!;
    private Button _btnPause = null!;
    private readonly System.Windows.Forms.Timer _tick = new();

    public MainForm(AppHost host)
    {
        _host = host;
        Text = L.T("app");
        Icon = TrayIcons.App;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1000, 700);
        MinimumSize = new System.Drawing.Size(900, 600);
        Font = Theme.Body;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        DoubleBuffered = true;

        WheelGuard.Attach(_cbCamera);
        WireControls();
        BuildLayout();
        LoadSettingsIntoControls();

        _host.Monitor.StatusChanged += OnStatus;
        _host.Monitor.FrameAnalyzed += OnFrame;
        _host.CamerasChanged += () => SafeInvoke(() => { FillCameras(); RenderTemplates(); });
        VisibleChanged += (_, _) =>
        {
            _previewWanted = Visible;
            if (Visible) ScheduleScrollCheck(force: true);
        };

        _tick.Interval = 1000;
        _tick.Tick += (_, _) => RenderChips(_host.Monitor.Status);
        _tick.Start();
    }

    /// <summary>Handlers that must not be registered again when the UI is rebuilt (language change).</summary>
    private void WireControls()
    {
        _cbCamera.DropDownStyle = ComboBoxStyle.DropDownList;
        _cbCamera.SelectedIndexChanged += async (_, _) =>
        {
            if (_loadingControls || _cbCamera.SelectedItem is not CameraInfo cam) return;
            await _host.SelectCameraAsync(cam);
            RenderTemplates();
        };
        _nudPresent.ValueChanged += (_, _) => Apply(s => s.PresentIntervalSec = (double)_nudPresent.Value);
        _nudAbsent.ValueChanged += (_, _) => Apply(s => s.AbsentIntervalSec = (double)_nudAbsent.Value);
        _nudThreshold.ValueChanged += (_, _) => Apply(s => s.MatchThreshold = (double)_nudThreshold.Value);
        foreach (var (box, lang, name) in new[] { (_flagEn, "en", "English"), (_flagCs, "cs", "Čeština") })
        {
            box.Size = new System.Drawing.Size(30, 20);
            box.SizeMode = PictureBoxSizeMode.Zoom;
            box.Cursor = Cursors.Hand;
            box.Margin = new Padding(0, 0, 6, 0);
            new ToolTip().SetToolTip(box, name);
            box.Click += (_, _) => SwitchLanguage(lang);
        }
        _chkAutostart.CheckedChanged += (_, _) =>
        {
            if (_loadingControls) return;
            try { Autostart.Set(_chkAutostart.Checked); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("dlg.autostart"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }

    private void SwitchLanguage(string lang)
    {
        if (L.Lang == lang) return;
        Apply(s => s.Language = lang);
        L.Set(lang);
        RebuildUi();
    }

    private void RenderFlags()
    {
        _flagEn.Image = TrayIcons.Flag("en", L.Lang != "en");
        _flagCs.Image = TrayIcons.Flag("cs", L.Lang != "cs");
    }

    private void RebuildUi()
    {
        SuspendLayout();
        try
        {
            Controls.Clear();
            Text = L.T("app");
            BuildLayout();
            LoadSettingsIntoControls();
            _headerState = PresenceState.Stopped; // force the state icon to redraw
            OnStatus(_host.Monitor.Status);
        }
        finally { ResumeLayout(true); }
        ScheduleScrollCheck(force: true);
    }

    // ---------- layout ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Window };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16, 12, 16, 12), BackColor = Theme.Window };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(BuildLeftColumn(), 0, 0);
        content.Controls.Add(BuildRightColumn(), 1, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
    }

    /// <summary>Unobtrusive acknowledgement strip at the bottom of the window.</summary>
    private Control BuildFooter()
    {
        var claude = L.T("footer.claude");
        var tabidoo = L.T("footer.tabidoo");
        var text = L.T("footer.text", claude, tabidoo);
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentHover,
            VisitedLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0),
            UseMnemonic = false,
        };
        link.Links.Clear();
        link.Links.Add(text.IndexOf(claude, StringComparison.Ordinal), claude.Length, "https://claude.ai");
        link.Links.Add(text.IndexOf(tabidoo, StringComparison.Ordinal), tabidoo.Length, "https://tabidoo.cloud");
        link.LinkClicked += (_, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo((string)e.Link!.LinkData!) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open link: " + ex.Message); }
        };

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Theme.Card,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        footer.Controls.Add(link, 0, 0);
        return footer;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Theme.Card,
            Padding = new Padding(16, 12, 16, 0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        _stateIcon.Size = new System.Drawing.Size(44, 44);
        _stateIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _stateIcon.Margin = new Padding(0, 0, 14, 0);
        _stateIcon.Anchor = AnchorStyles.Left;
        _stateIcon.Image ??= TrayIcons.StateBitmap(PresenceState.Starting, 48);
        header.Controls.Add(_stateIcon, 0, 0);

        var texts = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = new Padding(0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _lblState.AutoSize = true;
        _lblState.Font = Theme.Title;
        if (string.IsNullOrEmpty(_lblState.Text)) _lblState.Text = L.T("state.starting");
        _lblState.Margin = new Padding(0);
        _lblMessage.AutoSize = true;
        _lblMessage.ForeColor = Theme.Muted;
        _lblMessage.Margin = new Padding(1, 0, 0, 0);
        texts.Controls.Add(_lblState);
        texts.Controls.Add(_lblMessage);
        header.Controls.Add(texts, 1, 0);

        var flags = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 2, 0, 0), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        flags.Controls.Add(_flagEn);
        flags.Controls.Add(_flagCs);
        _flagCs.Margin = new Padding(0);
        RenderFlags();
        header.Controls.Add(flags, 2, 0);

        // stats row + Pause on the right, with a line on top
        var statsRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 10, 0, 0),
            Padding = new Padding(0, 8, 0, 8),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statsRow.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, statsRow.Width, 0);
        };
        var strip = BuildStatsStrip();
        statsRow.Controls.Add(strip, 0, 0);
        _btnPause = Theme.Secondary(L.T(_host.Monitor.IsPaused ? "btn.resume" : "btn.pause"), (_, _) => PauseToggleRequested?.Invoke());
        _btnPause.Anchor = AnchorStyles.Right;
        _btnPause.Margin = new Padding(12, 0, 0, 0);
        statsRow.Controls.Add(_btnPause, 1, 0);
        header.Controls.Add(statsRow, 0, 1);
        header.SetColumnSpan(statsRow, 3);
        return header;
    }

    /// <summary>Strip with six equally wide fields – values change, nothing jumps around.</summary>
    private Control BuildStatsStrip()
    {
        var strip = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 6,
            RowCount = 2,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var cells = new[]
        {
            ("stat.score", _statScore), ("stat.idle", _statIdle), ("stat.next", _statNext),
            ("stat.lock", _statLock), ("stat.inputs", _statInputs), ("stat.camera", _statCamera),
        };
        for (var i = 0; i < cells.Length; i++)
        {
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cells.Length));
            var caption = new Label { Text = L.T(cells[i].Item1), AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 8, 0) };
            var value = cells[i].Item2;
            value.AutoSize = false;
            value.Height = 20;
            value.Font = new Font("Segoe UI Semibold", 9.75f);
            value.ForeColor = Theme.Text;
            value.Margin = new Padding(0, 0, 8, 0);
            value.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            value.AutoEllipsis = true;
            if (string.IsNullOrEmpty(value.Text)) value.Text = L.T("stat.none");
            strip.Controls.Add(caption, i, 0);
            strip.Controls.Add(value, i, 1);
        }
        return strip;
    }

    private Control BuildLeftColumn()
    {
        var col = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };

        // the preview keeps 16:9 based on the column width
        var previewCard = new Panel { Dock = DockStyle.Top, Height = 240, BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(1) };
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.FromArgb(17, 24, 39);
        previewCard.Controls.Add(_preview);

        var camCard = new Card(L.T("card.camera")) { Dock = DockStyle.Top, Margin = new Padding(0) };
        var camRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        camRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        camRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _cbCamera.Font = Theme.Body;
        _cbCamera.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _cbCamera.Margin = new Padding(0, 2, 8, 0);
        _cbCamera.Width = 120;
        camRow.Controls.Add(_cbCamera, 0, 0);
        var refresh = Theme.Secondary(L.T("btn.refresh"), async (_, _) => await _host.RefreshCamerasAsync());
        refresh.Margin = new Padding(0);
        camRow.Controls.Add(refresh, 1, 0);
        camCard.AddFull(camRow);
        camCard.AddHintFull(L.T("hint.camera"));

        var tools = new Card(null) { Dock = DockStyle.Top, Margin = new Padding(0) };
        // three equally wide buttons in one row
        var toolButtons = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 4) };
        var buttons = new[]
        {
            Theme.Secondary(L.T("btn.about"), (_, _) => ShowAbout()),
            Theme.Secondary(L.T("btn.data_folder"), (_, _) => OpenDataFolder()),
            Theme.Secondary(L.T("btn.diagnostics"), (_, _) => new ProbeForm(_host).Show(this)),
        };
        for (var i = 0; i < buttons.Length; i++)
        {
            toolButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
            var b = buttons[i];
            b.AutoSize = false;
            b.Height = 34;
            b.Dock = DockStyle.Fill;
            b.AutoEllipsis = true;
            b.Margin = new Padding(i == 0 ? 0 : 4, 0, i == buttons.Length - 1 ? 0 : 4, 0);
            toolButtons.Controls.Add(b, i, 0);
        }
        tools.AddFull(toolButtons);
        tools.AddHintFull(L.T("hint.app"));

        // Preview 16:9 by width, but never so tall that the Camera and tools cards below it do not fit.
        void FitPreview()
        {
            var byWidth = (int)(col.ClientSize.Width * 9.0 / 16.0);
            var room = col.ClientSize.Height - camCard.Height - tools.Height - 24;
            var h = Math.Max(120, Math.Min(byWidth, room));
            if (previewCard.Height != h) previewCard.Height = h;
        }
        col.Resize += (_, _) => FitPreview();
        camCard.SizeChanged += (_, _) => FitPreview();
        tools.SizeChanged += (_, _) => FitPreview();

        // Docking is applied from the last added control: preview on top, then Camera, then tools.
        col.Controls.Add(tools);
        col.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12 });
        col.Controls.Add(camCard);
        col.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12 });
        col.Controls.Add(previewCard);
        return col;
    }

    private Control BuildRightColumn()
    {
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = false, Margin = new Padding(0) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        _scroller = scroller;
        _stack = stack;
        scroller.Resize += (_, _) => ScheduleScrollCheck();
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // --- Enrollment ---
        var enroll = new Card(L.T("card.enroll"));
        _lblTemplates.AutoSize = true;
        _lblTemplates.Margin = new Padding(0, 0, 0, 10);
        enroll.AddWrappingFull(_lblTemplates);
        var enrollButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        _btnEnroll = Theme.Primary(L.T(_enrollCts != null ? "btn.enroll_cancel" : "btn.enroll"), async (_, _) => await EnrollAsync());
        _btnClear = Theme.Secondary(L.T("btn.clear"), (_, _) => ClearTemplates());
        _btnClear.Enabled = _enrollCts == null;
        enrollButtons.Controls.Add(_btnEnroll);
        enrollButtons.Controls.Add(_btnClear);
        enroll.AddFull(enrollButtons);
        enroll.AddHintFull(L.T("hint.enroll"));
        stack.Controls.Add(enroll);

        // --- Presence check ---
        var presence = new Card(L.T("card.presence"));
        presence.AddRow(L.T("lbl.present_interval"), _nudPresent);
        presence.AddRow(L.T("lbl.absent_interval"), _nudAbsent);
        presence.AddRow(L.T("lbl.threshold"), _nudThreshold, L.T("hint.threshold"));
        stack.Controls.Add(presence);

        // --- Application ---
        var app = new Card(L.T("card.app"));
        _chkAutostart.Text = L.T("chk.autostart");
        _chkAutostart.AutoSize = true;
        _chkAutostart.Margin = new Padding(0, 0, 0, 2);
        app.AddFull(_chkAutostart);
        app.Margin = new Padding(0);
        stack.Controls.Add(app);

        scroller.Controls.Add(stack);
        return scroller;
    }

    private Panel? _scroller;
    private TableLayoutPanel? _stack;
    private bool _scrollCheckPending;

    private System.Drawing.Size _lastScrollCheckSize;

    private void ScheduleScrollCheck(bool force = false)
    {
        if (force) _lastScrollCheckSize = System.Drawing.Size.Empty;
        if (_scrollCheckPending || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        _scrollCheckPending = true;
        BeginInvoke(() =>
        {
            _scrollCheckPending = false;
            UpdateScrollbar();
        });
    }

    /// <summary>
    /// WinForms AutoScroll "keeps" its scrollbar: it narrows the content, hints wrap to more lines and the content really
    /// overflows. So the content height is measured at full width and the scrollbar is enabled only when it does not fit.
    /// Runs only when the outer panel size really changes – toggling the scrollbar does not change it, so it cannot loop.
    /// </summary>
    private void UpdateScrollbar()
    {
        if (_scroller == null || _stack == null || _scroller.IsDisposed || WindowState == FormWindowState.Minimized) return;
        if (_scroller.Size == _lastScrollCheckSize || _scroller.Width < 50 || _scroller.Height < 50) return;
        _lastScrollCheckSize = _scroller.Size;
        _scroller.AutoScroll = false;
        _stack.PerformLayout();
        foreach (Control c in _stack.Controls) c.PerformLayout(); // Card.OnLayout recalculates the hint widths
        _stack.PerformLayout();
        if (_stack.Height > _scroller.ClientSize.Height) _scroller.AutoScroll = true;
    }

    // ---------- settings <-> controls ----------

    private void LoadSettingsIntoControls()
    {
        _loadingControls = true;
        try
        {
            var s = _host.Settings;
            _nudPresent.Value = Clamp(_nudPresent, (decimal)s.PresentIntervalSec);
            _nudAbsent.Value = Clamp(_nudAbsent, (decimal)s.AbsentIntervalSec);
            _nudThreshold.Value = Clamp(_nudThreshold, (decimal)s.MatchThreshold);
            try { _chkAutostart.Checked = Autostart.IsEnabled(); } catch { }
            RenderTemplates();
            FillCameras();
        }
        finally { _loadingControls = false; }
    }

    private static decimal Clamp(NumericUpDown nud, decimal v) => Math.Min(nud.Maximum, Math.Max(nud.Minimum, v));

    private void Apply(Action<AppSettings> change)
    {
        if (_loadingControls) return;
        change(_host.Settings);
        _host.Settings.Save();
    }

    private void FillCameras()
    {
        var was = _loadingControls;
        _loadingControls = true;
        try
        {
            _cbCamera.Items.Clear();
            foreach (var cam in _host.Cameras) _cbCamera.Items.Add(cam);
            var current = _host.Monitor.Camera ?? _host.PreferredCamera();
            if (current != null)
            {
                var idx = _host.Cameras.ToList().FindIndex(c => c.SourceId == current.SourceId);
                if (idx >= 0) _cbCamera.SelectedIndex = idx;
            }
        }
        finally { _loadingControls = was; }
    }

    private void RenderTemplates()
    {
        var all = _host.Settings.Templates;
        var kind = (_host.Monitor.Camera ?? _host.PreferredCamera())?.Kind ?? "IR";
        var mine = all.Where(t => t.CameraKind == kind).ToList();

        if (mine.Count > 0)
        {
            var when = mine.Max(t => t.Created).ToString(L.T("date.format"), System.Globalization.CultureInfo.InvariantCulture);
            _lblTemplates.Text = L.T("enroll.status_ok", L.T("kind." + kind), when, mine.Count);
            _lblTemplates.ForeColor = Theme.Success;
            _lblTemplates.Font = Theme.BodyStrong;
        }
        else
        {
            var others = all.Select(t => L.T("kind." + t.CameraKind)).Distinct().ToList();
            _lblTemplates.Text = others.Count > 0
                ? L.T("enroll.status_other", L.T("kind." + kind), string.Join(", ", others))
                : L.T("enroll.status_none");
            _lblTemplates.ForeColor = Theme.Warning;
            _lblTemplates.Font = Theme.Body;
        }

        if (_enrollCts == null && _btnEnroll != null)
        {
            _btnEnroll.Text = L.T(mine.Count > 0 ? "btn.reenroll" : "btn.enroll");
            Theme.SetPrimary(_btnEnroll, mine.Count == 0);
        }
        if (_btnClear != null) _btnClear.Visible = all.Count > 0;
    }

    // ---------- actions ----------

    private async Task EnrollAsync()
    {
        if (_enrollCts != null)
        {
            _enrollCts.Cancel();
            return;
        }
        _enrollCts = new CancellationTokenSource();
        _btnEnroll.Text = L.T("btn.enroll_cancel");
        Theme.SetPrimary(_btnEnroll, false);
        _btnClear.Enabled = false;
        try
        {
            var progress = new Progress<string>(m => _lblMessage.Text = L.T("enroll.progress", m));
            var result = await _host.Monitor.EnrollAsync(10, progress, _enrollCts.Token);
            RenderTemplates();
            MessageBox.Show(this, result.Message, L.T("dlg.enroll"), MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _enrollCts.Dispose();
            _enrollCts = null;
            _btnClear.Enabled = true;
            RenderTemplates();
        }
    }

    private void ClearTemplates()
    {
        if (MessageBox.Show(this, L.T("dlg.clear_q"), L.T("app"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _host.Settings.Templates = new List<FaceTemplate>();
        _host.Settings.Save();
        RenderTemplates();
    }

    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(Log.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Log.Directory) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("Open data dir: " + ex.Message); }
    }

    private void ShowAbout()
    {
        var v = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?";
        if (MessageBox.Show(this, L.T("about.text", v, Log.Directory), L.T("dlg.about"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            return;
        var text = TextViewerForm.ReadResource("LICENSE.txt") + "\n\n\n" + TextViewerForm.ReadResource("THIRD-PARTY-NOTICES.txt");
        new TextViewerForm(L.T("dlg.licenses"), text).Show(this);
    }

    public void OnPauseChanged(bool paused) => SafeInvoke(() => _btnPause.Text = L.T(paused ? "btn.resume" : "btn.pause"));

    // ---------- status / preview ----------

    private void OnStatus(StatusSnapshot s) => SafeInvoke(() =>
    {
        _lblState.ForeColor = TrayIcons.ColorFor(s.State);
        if (_headerState != s.State)
        {
            _headerState = s.State;
            var old = _stateIcon.Image;
            _stateIcon.Image = TrayIcons.StateBitmap(s.State, 48);
            old?.Dispose();
        }
        if (_enrollCts == null) _lblMessage.Text = s.Message;
        RenderChips(s);
    });

    private void RenderChips(StatusSnapshot s)
    {
        var idle = ActivityKeeper.GetIdleTime();
        var rule = LockPolicy.Get();

        _lblState.Text = s.State switch
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

        _statScore.Text = s.Score.HasValue ? $"{s.Score.Value:F2} / {_host.Settings.MatchThreshold:F2}" : L.T("stat.none");
        _statIdle.Text = $"{idle.TotalSeconds:F0} s";
        var showNext = s.NextCheck.HasValue && s.State is PresenceState.Present or PresenceState.Absent or PresenceState.NotEnrolled;
        _statNext.Text = showNext ? $"{Math.Max(0, (s.NextCheck!.Value - DateTime.Now).TotalSeconds):F0} s" : L.T("stat.none");
        _statLock.Text = rule != null ? $"{L.Duration(rule.Timeout)} · {rule.SourceName}" : L.T("stat.lock_unknown");
        _statInputs.Text = s.Injections > 0 ? L.T("stat.inputs_val", s.Injections, s.LastInjection?.ToString("HH:mm:ss") ?? "") : "0";
        _statCamera.Text = string.IsNullOrEmpty(s.CameraFormat) ? L.T("stat.none") : s.CameraFormat;
    }

    private void OnFrame(Mat frame, FaceResult? face, double? score)
    {
        if (!_previewWanted) return;
        try
        {
            using var canvas = frame.Clone();
            if (face != null)
            {
                var ok = score.HasValue && score.Value >= _host.Settings.MatchThreshold;
                var color = ok ? new Scalar(67, 160, 46) : new Scalar(20, 140, 230);
                DrawCornerBox(canvas, face.Box, color);
                var label = score.HasValue ? $"{score.Value:F2}" : $"det {face.Score:F2}";
                Cv2.PutText(canvas, label, new CvPoint(face.Box.X, Math.Max(14, face.Box.Y - 8)),
                    HersheyFonts.HersheySimplex, 0.55, color, 2, LineTypes.AntiAlias);
            }
            Cv2.ImEncode(".bmp", canvas, out var bytes);
            using var ms = new MemoryStream(bytes);
            using var tmp = new Bitmap(ms);
            var bitmap = new Bitmap(tmp);
            SafeInvoke(() =>
            {
                var old = _preview.Image;
                _preview.Image = bitmap;
                old?.Dispose();
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

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    // ---------- window ----------

    private FormWindowState _lastWindowState = FormWindowState.Normal;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != _lastWindowState)
        {
            Log.Write($"Window state {_lastWindowState} -> {WindowState}");
            _lastWindowState = WindowState;
            if (WindowState != FormWindowState.Minimized) ScheduleScrollCheck(force: true);
        }
    }

    /// <summary>Shows the window in front; if it is outside all monitors (disconnected monitor), moves it to the primary screen.</summary>
    public void ShowAndActivate()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(Bounds)))
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            Location = new System.Drawing.Point(wa.X + Math.Max(0, (wa.Width - Width) / 2), wa.Y + Math.Max(0, (wa.Height - Height) / 2));
            Log.Write("Window was off-screen, moved to the primary screen");
        }
        BringToFront();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _tick.Stop();
        _host.Monitor.StatusChanged -= OnStatus;
        _host.Monitor.FrameAnalyzed -= OnFrame;
        base.OnFormClosing(e);
    }

    public void ForceClose()
    {
        _reallyClose = true;
        Close();
    }
}

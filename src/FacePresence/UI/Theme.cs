namespace FacePresence.UI;

/// <summary>Barvy, fonty a tovární metody pro jednotný vzhled (světlé "karty" na šedém pozadí, indigo akcent).</summary>
internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(243, 244, 246);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(229, 231, 235);
    public static readonly Color Text = Color.FromArgb(17, 24, 39);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Accent = Color.FromArgb(67, 56, 202);
    public static readonly Color AccentHover = Color.FromArgb(55, 48, 163);
    public static readonly Color ChipBack = Color.FromArgb(238, 242, 255);
    public static readonly Color ChipText = Color.FromArgb(55, 48, 163);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 10.5f);
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 9.75f);

    public static Button Primary(string text, EventHandler onClick)
    {
        var b = Secondary(text, onClick);
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        b.FlatAppearance.MouseDownBackColor = AccentHover;
        return b;
    }

    /// <summary>Přepne vzhled existujícího tlačítka mezi primárním (indigo) a sekundárním (bílým).</summary>
    public static void SetPrimary(Button b, bool primary)
    {
        b.BackColor = primary ? Accent : Card;
        b.ForeColor = primary ? Color.White : Text;
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(249, 250, 251);
        b.FlatAppearance.MouseDownBackColor = primary ? AccentHover : Color.FromArgb(243, 244, 246);
    }

    public static readonly Color Success = Color.FromArgb(22, 128, 61);
    public static readonly Color Warning = Color.FromArgb(180, 83, 9);

    public static Button Secondary(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Card,
            ForeColor = Text,
            Font = Body,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false,
            UseMnemonic = false,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(249, 250, 251);
        b.Click += onClick;
        return b;
    }

    public static Label Chip(string text = "")
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            BackColor = ChipBack,
            ForeColor = ChipText,
            Font = Small,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 0, 6, 6),
        };
    }

    public static NumericUpDown Numeric(decimal min, decimal max, int decimals, decimal increment)
    {
        return WheelGuard.Attach(new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = increment,
            Width = 96,
            Font = Body,
            BorderStyle = BorderStyle.FixedSingle,
        });
    }
}

/// <summary>
/// Kolečko myši nad NumericUpDown/ComboBox normálně mění hodnotu (i při pouhém rolování stránky).
/// Tady ho předáme nejbližšímu rolovatelnému rodiči.
/// </summary>
internal static class WheelGuard
{
    public static T Attach<T>(T control) where T : Control
    {
        control.MouseWheel += (sender, e) =>
        {
            if (e is HandledMouseEventArgs h) h.Handled = true;
            for (var parent = control.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is ScrollableControl { AutoScroll: true } sc)
                {
                    sc.AutoScrollPosition = new Point(0, -sc.AutoScrollPosition.Y - e.Delta);
                    break;
                }
            }
        };
        return control;
    }
}

/// <summary>Bílá karta s nadpisem, 1px rámečkem a dvousloupcovou mřížkou popisek / ovládací prvek.</summary>
internal sealed class Card : TableLayoutPanel
{
    private const int LabelColumnWidth = 230;
    private readonly List<(Label label, bool full)> _hints = new();

    public Card(string title)
    {
        BackColor = Theme.Card;
        ColumnCount = 2;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 10, 16, 12);
        Margin = new Padding(0, 0, 0, 10);
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        var heading = new Label
        {
            Text = title,
            Font = Theme.CardTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        AddFull(heading);
    }

    public void AddRow(string label, Control control, string? hint = null)
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            Font = Theme.Body,
            ForeColor = Theme.Text,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 8, 3),
        };
        Controls.Add(lbl, 0, row);
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(0, 3, 0, 3);
        Controls.Add(control, 1, row);
        if (hint != null) AddHint(hint);
    }

    public void AddFull(Control control)
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(control, 0, row);
        SetColumnSpan(control, 2);
    }

    /// <summary>Label přes oba sloupce, jehož šířka se drží šířky karty (zalamuje se místo přetékání).</summary>
    public void AddWrappingFull(Label lbl)
    {
        _hints.Add((lbl, true));
        AddFull(lbl);
    }

    public void AddHintFull(string text)
    {
        var lbl = HintLabel(text);
        _hints.Add((lbl, true));
        AddFull(lbl);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        // Label s AutoSize se jinak roztáhne na MaximumSize a karta přeteče sloupec – šířku nápověd vážeme na šířku karty.
        var inner = Math.Max(120, ClientSize.Width - Padding.Horizontal);
        foreach (var (label, full) in _hints)
        {
            var w = full ? inner : Math.Max(120, inner - LabelColumnWidth);
            if (label.MaximumSize.Width != w) label.MaximumSize = new Size(w, 0);
        }
        base.OnLayout(e);
    }

    private static Label HintLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Small,
        ForeColor = Theme.Muted,
        MaximumSize = new Size(320, 0),
        Margin = new Padding(0, 2, 0, 4),
    };

    public void AddHint(string text)
    {
        var lbl = HintLabel(text);
        lbl.Margin = new Padding(0, 0, 0, 6);
        _hints.Add((lbl, false));
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(lbl, 1, row);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

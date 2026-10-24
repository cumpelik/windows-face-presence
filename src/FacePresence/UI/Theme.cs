namespace FacePresence.UI;

/// <summary>Colors, fonts and factory methods for a consistent look (light "cards" on a gray background, indigo accent).</summary>
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

    /// <summary>Switches an existing button between the primary (indigo) and secondary (white) look.</summary>
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
/// The mouse wheel over a NumericUpDown/ComboBox normally changes its value (even when just scrolling the page).
/// Here it is passed to the nearest scrollable parent instead.
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

/// <summary>White card with an optional title, a 1 px border and a two-column label / control grid.</summary>
internal sealed class Card : TableLayoutPanel
{
    private const int LabelColumnWidth = 230;

    public Card(string? title)
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

        if (title == null) return;
        AddFull(new Label
        {
            Text = title,
            Font = Theme.CardTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        });
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
        if (hint != null) AddCell(Hint(text: hint, bottom: 6), column: 1, span: 1);
    }

    /// <summary>A control spanning both columns. Labels fill the cell, so the table wraps them to the card width.</summary>
    public void AddFull(Control control) => AddCell(control, column: 0, span: 2);

    public void AddHintFull(string text) => AddFull(Hint(text, bottom: 4));

    private void AddCell(Control control, int column, int span)
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (control is Label) control.Dock = DockStyle.Fill;
        else control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(control, column, row);
        SetColumnSpan(control, span);
    }

    private static Label Hint(string text, int bottom) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Small,
        ForeColor = Theme.Muted,
        Margin = new Padding(0, 2, 0, bottom),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

namespace FacePresence.UI;

/// <summary>Simple window for showing long text (licenses).</summary>
public sealed class TextViewerForm : Form
{
    public TextViewerForm(string title, string text)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new System.Drawing.Size(820, 600);
        Font = Theme.Body;
        Icon = App.TrayIcons.App;
        BackColor = Theme.Window;

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9.5f),
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Theme.Window };
        frame.Controls.Add(box);
        Controls.Add(frame);
        Shown += (_, _) => { box.SelectionStart = 0; box.SelectionLength = 0; };
    }

    public static string ReadResource(string name)
    {
        using var stream = typeof(TextViewerForm).Assembly.GetManifestResourceStream(name);
        if (stream == null) return name + " not found.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

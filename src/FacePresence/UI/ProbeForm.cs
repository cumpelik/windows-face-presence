using FacePresence.App;
using FacePresence.Diagnostics;
using FacePresence.Localization;

namespace FacePresence.UI;

public sealed class ProbeForm : Form
{
    private readonly AppHost _host;
    private readonly TextBox _output = new();
    private readonly Button _run;
    private readonly Button _copy;
    private CancellationTokenSource? _cts;

    public ProbeForm(AppHost host)
    {
        _host = host;
        Text = L.T("probe.title");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new System.Drawing.Size(860, 580);
        Font = Theme.Body;
        Icon = TrayIcons.App;
        BackColor = Theme.Window;

        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Both;
        _output.WordWrap = false;
        _output.Font = new Font("Consolas", 9.5f);
        _output.Dock = DockStyle.Fill;
        _output.BorderStyle = BorderStyle.None;
        _output.BackColor = Theme.Card;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10, 8, 10, 8), BackColor = Theme.Window };
        _run = Theme.Primary(L.T("btn.run"), async (_, _) => await RunAsync());
        _copy = Theme.Secondary(L.T("btn.copy"), (_, _) => { try { Clipboard.SetText(_output.Text); } catch { } });
        bar.Controls.Add(_run);
        bar.Controls.Add(_copy);

        var intro = new Label
        {
            Text = L.T("probe.intro").Trim(),
            AutoSize = true,
            Dock = DockStyle.Fill, // fills the table cell, so the table wraps it to the window width
            ForeColor = Theme.Text,
            Margin = new Padding(12, 12, 12, 8),
        };
        _output.Margin = new Padding(10, 0, 10, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Window };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(_output, 0, 1);

        Controls.Add(layout);
        Controls.Add(bar);
    }

    private async Task RunAsync()
    {
        _run.Enabled = false;
        _output.Clear();
        _cts = new CancellationTokenSource();
        var wasRunning = _host.Monitor.IsRunning;
        var camera = _host.Monitor.Camera;
        try
        {
            if (wasRunning) await _host.StopAsync(); // release the camera for the test
            var progress = new Progress<string>(line => _output.AppendText(line + Environment.NewLine));
            var text = await Probe.RunAsync(_host, progress, _cts.Token);
            _output.Text = text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n")
                           + Environment.NewLine + L.T("probe.saved", Path.Combine(Log.Directory, "probe.txt")) + Environment.NewLine;
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _output.AppendText(L.T("probe.error", ex) + Environment.NewLine);
        }
        finally
        {
            if (wasRunning && camera != null) await _host.SelectCameraAsync(camera);
            _cts.Dispose();
            _cts = null;
            _run.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}

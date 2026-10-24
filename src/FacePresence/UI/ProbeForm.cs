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
        _output.Text = L.T("probe.intro");

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10, 8, 10, 8), BackColor = Theme.Window };
        _run = Theme.Primary(L.T("btn.run"), async (_, _) => await RunAsync());
        _copy = Theme.Secondary(L.T("btn.copy"), (_, _) => { try { Clipboard.SetText(_output.Text); } catch { } });
        bar.Controls.Add(_run);
        bar.Controls.Add(_copy);

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 0), BackColor = Theme.Window };
        frame.Controls.Add(_output);
        Controls.Add(frame);
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
            if (wasRunning) await _host.StopAsync(); // uvolnit kameru pro test
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

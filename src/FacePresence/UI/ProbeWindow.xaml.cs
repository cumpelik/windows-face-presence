using System.IO;
using System.Windows;
using FacePresence.App;
using FacePresence.Diagnostics;
using FacePresence.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FacePresence.UI;

public partial class ProbeWindow : FluentWindow
{
    private readonly AppHost _host;
    private CancellationTokenSource? _cts;

    public ProbeWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = TrayIcons.AppImage;
        TitleBar.Icon = new ImageIcon { Source = TrayIcons.AppImage };
        SourceInitialized += (_, _) => SystemThemeWatcher.Watch(this);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        Output.Clear();
        _cts = new CancellationTokenSource();
        var wasRunning = _host.Monitor.IsRunning;
        var camera = _host.Monitor.Camera;
        try
        {
            if (wasRunning) await _host.StopAsync(); // release the camera for the test
            var progress = new Progress<string>(line =>
            {
                Output.AppendText(line + Environment.NewLine);
                Output.ScrollToEnd();
            });
            var text = await Probe.RunAsync(_host, progress, _cts.Token);
            Output.Text = text + Environment.NewLine + L.T("probe.saved", Path.Combine(Log.Directory, "probe.txt")) + Environment.NewLine;
            Output.ScrollToEnd();
        }
        catch (Exception ex)
        {
            Output.AppendText(L.T("probe.error", ex) + Environment.NewLine);
        }
        finally
        {
            if (wasRunning && camera != null) await _host.SelectCameraAsync(camera);
            _cts.Dispose();
            _cts = null;
            RunButton.IsEnabled = true;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Output.Text); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}

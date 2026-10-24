using System.Windows;
using System.Windows.Threading;
using StillHere.App;
using StillHere.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace StillHere;

internal static class Program
{
    private const string MutexName = @"Local\StillHere.SingleInstance";
    private const string ShowEventName = @"Local\StillHere.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Already running – just ask it to show its window (works even when the window got "lost").
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                signal.Set();
            }
            catch
            {
                MessageBox.Show(L.T("already_running"), "Still Here", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        Settings.Autostart.Sync();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Unhandled: " + e.ExceptionObject);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("UI exception: " + e.Exception);
            e.Handled = true;
        };
        // Fluent (Windows 11) styles for all windows; light/dark follows the system.
        app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        app.Resources.MergedDictionaries.Add(new ControlsDictionary());
        ApplicationThemeManager.ApplySystemTheme();

        var minimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        Log.Write($"Start v{typeof(Program).Assembly.GetName().Version} minimized={minimized}");

        TrayHost? tray = null;
        app.Startup += (_, _) =>
        {
            try
            {
                tray = new TrayHost(minimized, showSignal);
            }
            catch (Exception ex)
            {
                // Without the tray host there is no window and no tray icon – never keep running invisibly.
                Log.Write("Startup failed: " + ex);
                MessageBox.Show(L.T("startup_failed", ex.Message, Log.FilePath), "Still Here", MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown(1);
            }
        };
        app.Run();
        GC.KeepAlive(tray);
        Log.Write("Exit");
    }
}

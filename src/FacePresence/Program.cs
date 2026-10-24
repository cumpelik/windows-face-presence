using FacePresence.App;

namespace FacePresence;

internal static class Program
{
    private const string MutexName = @"Local\FacePresence.SingleInstance";
    private const string ShowEventName = @"Local\FacePresence.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Už běží – jen ji požádáme o zobrazení okna (funguje i když okno "zmizelo").
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                signal.Set();
            }
            catch
            {
                MessageBox.Show(Localization.L.T("already_running"), "Face Presence", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("UI exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Unhandled: " + e.ExceptionObject);

        var minimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        Log.Write($"Start v{typeof(Program).Assembly.GetName().Version} minimized={minimized}");
        Application.Run(new TrayContext(minimized, showSignal));
        Log.Write("Exit");
    }
}

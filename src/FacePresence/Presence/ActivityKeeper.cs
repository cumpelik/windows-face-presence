using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FacePresence.Native;
using static FacePresence.Native.NativeMethods;

namespace FacePresence.Presence;

public enum KeepAliveMethod
{
    /// <summary>SendInput s relativním pohybem myši 0/0 – kurzor se nehne, systém ale vidí vstup.</summary>
    ZeroMouseMove = 0,
    /// <summary>Pohyb myši o 1 px a hned zpět (záloha, kdyby nulový pohyb nestačil).</summary>
    MouseJiggle = 1,
}

/// <summary>
/// Drží Windows "vzhůru", dokud je uživatel přítomen:
///  1) SetThreadExecutionState(ES_DISPLAY_REQUIRED) – brání vypnutí displeje / spořiči (bez simulace vstupu),
///  2) minimální simulovaný vstup přes SendInput, ale jen když skutečná nečinnost (GetLastInputInfo)
///     překročí práh – to je jediná cesta, jak obejít "Interactive logon: Machine inactivity limit",
///     která měří pouze klávesnici/myš.
/// Po Release()/Dispose() se vše vrátí do standardního chování Windows.
/// </summary>
public sealed class ActivityKeeper : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private volatile bool _present;
    private int _injections;

    public int InjectionCount => _injections;
    public DateTime? LastInjection { get; private set; }

    public ActivityKeeper()
    {
        // ES_CONTINUOUS je vázané na vlákno – proto vlastní dlouhožijící vlákno.
        _thread = new Thread(Loop) { IsBackground = true, Name = "ActivityKeeper" };
        _thread.Start();
    }

    private void Loop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception ex) { Log.Write("ActivityKeeper: " + ex.Message); }
        }
        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
    }

    /// <summary>Skutečná doba od posledního vstupu uživatele v této session.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var now = unchecked((uint)Environment.TickCount);
        return TimeSpan.FromMilliseconds(unchecked(now - info.dwTime));
    }

    public TimeSpan IdleTime => GetIdleTime();

    public void SetPresent(bool present)
    {
        if (_present == present) return;
        _present = present;
        if (_queue.IsAddingCompleted) return;
        _queue.Add(() =>
        {
            var flags = present
                ? EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED
                : EXECUTION_STATE.ES_CONTINUOUS;
            var prev = SetThreadExecutionState(flags);
            Log.Write($"SetThreadExecutionState({flags}) prev={prev}");
        });
    }

    /// <summary>Pošle simulovaný vstup jen pokud skutečná nečinnost přesáhla práh. Vrací true, když vstup odeslal.</summary>
    public bool InjectIfIdle(TimeSpan threshold, KeepAliveMethod method)
    {
        if (GetIdleTime() < threshold) return false;
        var ok = Inject(method);
        if (ok)
        {
            Interlocked.Increment(ref _injections);
            LastInjection = DateTime.Now;
        }
        return ok;
    }

    public static bool Inject(KeepAliveMethod method)
    {
        INPUT[] inputs = method switch
        {
            KeepAliveMethod.MouseJiggle => new[] { Mouse(1, 1), Mouse(-1, -1) },
            _ => new[] { Mouse(0, 0) },
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Log.Write($"SendInput selhal: sent={sent} err={Marshal.GetLastWin32Error()}");
        return sent == inputs.Length;
    }

    private static INPUT Mouse(int dx, int dy) => new()
    {
        type = INPUT_MOUSE,
        U = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE } },
    };

    public void Release() => SetPresent(false);

    public void Dispose()
    {
        Release();
        _queue.CompleteAdding();
        _thread.Join(2000);
    }
}

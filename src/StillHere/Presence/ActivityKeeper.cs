using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using StillHere.Native;
using static StillHere.Native.NativeMethods;

namespace StillHere.Presence;

public enum KeepAliveMethod
{
    /// <summary>SendInput with a relative mouse move of 0/0 – the cursor does not move, but the system sees input.</summary>
    ZeroMouseMove = 0,
    /// <summary>Mouse move by 1 px and back (fallback in case the zero move is not enough).</summary>
    MouseJiggle = 1,
}

/// <summary>
/// Keeps Windows "awake" while the user is present:
///  1) SetThreadExecutionState(ES_DISPLAY_REQUIRED) – prevents display off / screen saver (no simulated input),
///  2) a minimal simulated input via SendInput, but only when the real idle time (GetLastInputInfo) exceeds
///     the threshold – the only way to defer "Interactive logon: Machine inactivity limit", which counts
///     keyboard/mouse input only.
/// After Release()/Dispose() Windows behaves exactly as by default again.
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
        // ES_CONTINUOUS is bound to the calling thread, hence a dedicated long-lived thread.
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

    /// <summary>Real time since the user's last input in this session.</summary>
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

    /// <summary>Sends simulated input only when the real idle time exceeds the threshold. Returns true when input was sent.</summary>
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

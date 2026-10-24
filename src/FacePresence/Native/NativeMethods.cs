using System.Runtime.InteropServices;

namespace FacePresence.Native;

internal static class NativeMethods
{
    // ---- GetLastInputInfo ----
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // ---- SetThreadExecutionState ----
    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_CONTINUOUS = 0x80000000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    // ---- SendInput ----
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ---- Power schemes (lock estimate and diagnostics) ----
    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

    public const int SystemPowerCapabilities = 4;

    [DllImport("powrprof.dll")]
    public static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, int inputBufferSize, byte[] outputBuffer, int outputBufferSize);

    public static readonly Guid GUID_VIDEO_SUBGROUP = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid GUID_VIDEO_POWERDOWN_TIMEOUT = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    public static readonly Guid GUID_CONSOLE_LOCK_DISPLAY_OFF = new("8ec4b3a5-6868-48c2-be75-4f3044be88a7");
    public static readonly Guid GUID_SLEEP_SUBGROUP = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    public static readonly Guid GUID_STANDBY_TIMEOUT = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    public static readonly Guid GUID_NO_SUBGROUP = new("fea3413e-7e05-4911-9a71-700331f1c294");
    public static readonly Guid GUID_LOCK_CONSOLE_ON_WAKE = new("0e796bdb-100d-47d6-a2d5-f7d2daa51f51");
}

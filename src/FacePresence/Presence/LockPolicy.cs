using System.Runtime.InteropServices;
using FacePresence.Localization;
using FacePresence.Native;
using Microsoft.Win32;

namespace FacePresence.Presence;

public sealed record LockRule(TimeSpan Timeout, string SourceKey)
{
    public string SourceName => L.T(SourceKey);
}

/// <summary>
/// Odhad, po jaké době nečinnosti se Windows zamknou: minimum z politiky InactivityTimeoutSecs, zabezpečeného spořiče,
/// vypnutí displeje (jen u Modern Standby, kde vypnutí displeje = uspání) a uspání – vždy jen pokud je vyžadováno
/// přihlášení po probuzení. Jen odhad pro UI; nic se nemění.
/// </summary>
public static class LockPolicy
{
    private static readonly object Sync = new();
    private static LockRule? _cached;
    private static DateTime _cachedAt = DateTime.MinValue;

    public static LockRule? Get()
    {
        lock (Sync)
        {
            if (DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(30)) return _cached;
            try { _cached = Evaluate(); }
            catch (Exception ex) { Log.Write("LockPolicy: " + ex.Message); _cached = null; }
            _cachedAt = DateTime.UtcNow;
            return _cached;
        }
    }

    public static LockRule? Evaluate()
    {
        var rules = new List<LockRule>();

        var inactivity = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs");
        if (inactivity is > 0) rules.Add(new LockRule(TimeSpan.FromSeconds(inactivity.Value), "lock.policy"));

        string? Ss(string name) =>
            ReadString(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Control Panel\Desktop", name)
            ?? ReadString(Registry.CurrentUser, @"Control Panel\Desktop", name);
        if (Ss("ScreenSaveActive") == "1" && Ss("ScreenSaverIsSecure") == "1" && !string.IsNullOrEmpty(Ss("SCRNSAVE.EXE"))
            && int.TryParse(Ss("ScreenSaveTimeOut"), out var ssTimeout) && ssTimeout > 0)
            rules.Add(new LockRule(TimeSpan.FromSeconds(ssTimeout), "lock.screensaver"));

        var onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        var (lockAc, lockDc) = ReadPower(NativeMethods.GUID_NO_SUBGROUP, NativeMethods.GUID_LOCK_CONSOLE_ON_WAKE);
        var requireSignIn = (onBattery ? lockDc : lockAc) == 1;
        if (requireSignIn)
        {
            var (sleepAc, sleepDc) = ReadPower(NativeMethods.GUID_SLEEP_SUBGROUP, NativeMethods.GUID_STANDBY_TIMEOUT);
            var sleep = onBattery ? sleepDc : sleepAc;
            if (sleep is > 0) rules.Add(new LockRule(TimeSpan.FromSeconds(sleep.Value), "lock.sleep"));

            if (IsModernStandby())
            {
                var (videoAc, videoDc) = ReadPower(NativeMethods.GUID_VIDEO_SUBGROUP, NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT);
                var video = onBattery ? videoDc : videoAc;
                if (video is > 0)
                {
                    var delay = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DelayLockInterval") ?? 0;
                    rules.Add(new LockRule(TimeSpan.FromSeconds(video.Value + delay), "lock.display"));
                }
            }
        }

        return rules.Count == 0 ? null : rules.MinBy(r => r.Timeout);
    }

    /// <summary>Modern Standby (AoAc) – vypnutí displeje po nečinnosti je zároveň "uspání" a platí pro něj zámek po probuzení.</summary>
    public static bool IsModernStandby()
    {
        try
        {
            var buffer = new byte[76];
            var rc = NativeMethods.CallNtPowerInformation(NativeMethods.SystemPowerCapabilities, IntPtr.Zero, 0, buffer, buffer.Length);
            return rc == 0 && buffer[20] != 0; // SYSTEM_POWER_CAPABILITIES.AoAc
        }
        catch { return false; }
    }

    public static uint? ReadDword(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) is int i ? unchecked((uint)i) : null;
        }
        catch { return null; }
    }

    public static string? ReadString(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name)?.ToString();
        }
        catch { return null; }
    }

    public static (uint? ac, uint? dc) ReadPower(Guid subgroup, Guid setting)
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return (null, null);
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(ptr);
            uint? ac = NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var a) == 0 ? a : null;
            uint? dc = NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var d) == 0 ? d : null;
            return (ac, dc);
        }
        finally { NativeMethods.LocalFree(ptr); }
    }
}

using Microsoft.Win32;

namespace StillHere.Settings;

/// <summary>Start after sign-in via HKCU\...\Run (current user only, no admin rights needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StillHere";

    private static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
    }

    /// <summary>Keeps an enabled autostart pointing at this EXE (a new version may be saved under another name or path).</summary>
    public static void Sync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            var current = key.GetValue(ValueName) as string;
            if (!string.IsNullOrWhiteSpace(current) && current != Command)
            {
                key.SetValue(ValueName, Command);
                Log.Write("Autostart now starts " + Command);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Autostart sync failed: " + ex.Message);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

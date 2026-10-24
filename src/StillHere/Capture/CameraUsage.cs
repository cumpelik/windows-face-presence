using Microsoft.Win32;

namespace StillHere.Capture;

/// <summary>
/// Which other apps are using a camera right now. Windows records camera use per app under
/// ...\CapabilityAccessManager\ConsentStore\webcam: while an app has the camera open, its LastUsedTimeStop is 0.
/// </summary>
public static class CameraUsage
{
    private const string StoreKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    /// <summary>Names of the other apps that currently have a camera open (empty when none or unknown).</summary>
    public static IReadOnlyList<string> OtherAppsUsingCamera()
    {
        var self = (Environment.ProcessPath ?? "").Replace('\\', '#');
        var apps = new List<string>();
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var store = hive.OpenSubKey(StoreKey);
                if (store == null) continue;
                foreach (var name in store.GetSubKeyNames())
                {
                    using var app = store.OpenSubKey(name);
                    if (app == null) continue;
                    if (name == "NonPackaged")
                    {
                        foreach (var exe in app.GetSubKeyNames())
                        {
                            if (string.Equals(exe, self, StringComparison.OrdinalIgnoreCase)) continue;
                            using var key = app.OpenSubKey(exe);
                            var path = exe.Replace('#', '\\');
                            // An app that crashed during a call leaves its entry "in use" – count it only while it runs.
                            if (InUse(key) && IsRunning(path)) apps.Add(Path.GetFileName(path));
                        }
                    }
                    else if (InUse(app))
                    {
                        apps.Add(name.Split('_')[0]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Camera usage check failed: " + ex.Message);
        }
        return apps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsRunning(string exePath)
    {
        var processes = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private static bool InUse(RegistryKey? key) =>
        key?.GetValue("LastUsedTimeStart") is long start && start > 0 &&
        key.GetValue("LastUsedTimeStop") is long stop && stop == 0;
}

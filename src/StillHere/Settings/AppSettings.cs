using System.Text.Json;
using System.Text.Json.Serialization;
using StillHere.Presence;

namespace StillHere.Settings;

public sealed class FaceTemplate
{
    public string CameraKind { get; set; } = "Color";
    public float[] Feature { get; set; } = Array.Empty<float>();
    public DateTime Created { get; set; }
}

/// <summary>Settings + enrolled face templates, stored as JSON in %LOCALAPPDATA%\StillHere\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(Log.Directory, "settings.json");

    /// <summary>Source id (MediaFrameSourceInfo.Id) of the last selected camera; null = automatic (IR preferred).</summary>
    public string? CameraSourceId { get; set; }

    public double PresentIntervalSec { get; set; } = 10;
    public double AbsentIntervalSec { get; set; } = 1;

    /// <summary>Minimum cosine similarity for "that is the user". SFace/LFW recommends 0.363; a stricter value is used for a single user.</summary>
    public double MatchThreshold { get; set; } = 0.40;

    public bool KeepAliveEnabled { get; set; } = true;

    /// <summary>After how many seconds of real idle time (GetLastInputInfo) to send the minimal input. Must be below the shortest lock timeout (usually 60 s).</summary>
    public double KeepAliveIdleSec { get; set; } = 40;

    public KeepAliveMethod KeepAliveMethod { get; set; } = KeepAliveMethod.ZeroMouseMove;

    /// <summary>"en" (default) or "cs".</summary>
    public string Language { get; set; } = "en";

    public List<FaceTemplate> Templates { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded != null) return loaded.Sanitize();
            }
        }
        catch (Exception ex)
        {
            Log.Write("Loading settings failed, using defaults: " + ex.Message);
        }
        return new AppSettings();
    }

    private AppSettings Sanitize()
    {
        PresentIntervalSec = Math.Clamp(PresentIntervalSec, 0.5, 300);
        AbsentIntervalSec = Math.Clamp(AbsentIntervalSec, 0.2, 60);
        MatchThreshold = Math.Clamp(MatchThreshold, 0.05, 0.95);
        // Not editable in the UI any more; values below 20 s came from an older version (one could scroll it down by
        // accident) and only make the app check and send input far too often – fall back to the default.
        if (KeepAliveIdleSec < 20) KeepAliveIdleSec = 40;
        KeepAliveIdleSec = Math.Min(KeepAliveIdleSec, 600);
        Language = Language == "cs" ? "cs" : "en";
        Templates ??= new List<FaceTemplate>();
        Templates.RemoveAll(t => t.Feature == null || t.Feature.Length == 0);
        return this;
    }

    public void Save()
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Log.Directory);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(tmp, FilePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Saving settings failed: " + ex.Message);
        }
    }
}

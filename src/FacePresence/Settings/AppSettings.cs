using System.Text.Json;
using System.Text.Json.Serialization;
using FacePresence.Presence;

namespace FacePresence.Settings;

public sealed class FaceTemplate
{
    public string CameraKind { get; set; } = "Color";
    public float[] Feature { get; set; } = Array.Empty<float>();
    public DateTime Created { get; set; }
}

/// <summary>Nastavení + registrované šablony obličeje, JSON v %LOCALAPPDATA%\FacePresence\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(Log.Directory, "settings.json");

    /// <summary>Id zdroje (MediaFrameSourceInfo.Id) naposledy zvolené kamery; null = automaticky (IR přednostně).</summary>
    public string? CameraSourceId { get; set; }

    public double PresentIntervalSec { get; set; } = 10;
    public double AbsentIntervalSec { get; set; } = 1;

    /// <summary>Minimální kosinová podobnost pro "to je on". SFace/LFW doporučuje 0.363; pro 1 uživatele volíme přísnější.</summary>
    public double MatchThreshold { get; set; } = 0.40;

    public bool KeepAliveEnabled { get; set; } = true;

    /// <summary>Po kolika sekundách skutečné nečinnosti (GetLastInputInfo) poslat minimální vstup. Musí být pod nejkratším lock timeoutem (obvykle 60 s).</summary>
    public double KeepAliveIdleSec { get; set; } = 40;

    public KeepAliveMethod KeepAliveMethod { get; set; } = KeepAliveMethod.ZeroMouseMove;

    /// <summary>"en" (výchozí) nebo "cs".</summary>
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
            Log.Write("Načtení nastavení selhalo, používám výchozí: " + ex.Message);
        }
        return new AppSettings();
    }

    private AppSettings Sanitize()
    {
        PresentIntervalSec = Math.Clamp(PresentIntervalSec, 0.5, 300);
        AbsentIntervalSec = Math.Clamp(AbsentIntervalSec, 0.2, 60);
        MatchThreshold = Math.Clamp(MatchThreshold, 0.05, 0.95);
        KeepAliveIdleSec = Math.Clamp(KeepAliveIdleSec, 5, 600);
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
            Log.Write("Uložení nastavení selhalo: " + ex.Message);
        }
    }
}

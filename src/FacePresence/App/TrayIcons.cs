using FacePresence.Presence;

namespace FacePresence.App;

/// <summary>Ikony aplikace a lišty (vložené .ico z Assets, generované skriptem docs/make_icons.py).</summary>
public static class TrayIcons
{
    private static readonly Dictionary<(string, int), Icon> Cache = new();

    public static Color ColorFor(PresenceState state) => state switch
    {
        PresenceState.Present => Color.FromArgb(46, 160, 67),
        PresenceState.Absent => Color.FromArgb(230, 140, 20),
        PresenceState.NotEnrolled => Color.FromArgb(40, 120, 200),
        PresenceState.Enrolling => Color.FromArgb(40, 120, 200),
        PresenceState.CameraError => Color.FromArgb(200, 40, 40),
        _ => Color.FromArgb(110, 110, 110),
    };

    private static string NameFor(PresenceState state) => state switch
    {
        PresenceState.Present => "tray_present",
        PresenceState.Absent => "tray_absent",
        PresenceState.NotEnrolled or PresenceState.Enrolling => "tray_info",
        PresenceState.CameraError => "tray_error",
        _ => "tray_idle",
    };

    /// <summary>Ikona do lišty ve velikosti odpovídající DPI.</summary>
    public static Icon Get(PresenceState state) => Load(NameFor(state), SystemInformation.SmallIconSize.Width);

    /// <summary>Ikona aplikace (okno, hlavní panel).</summary>
    public static Icon App => Load("app", 0);

    /// <summary>Bitmapa stavové ikony pro UI (např. 48 px v hlavičce okna).</summary>
    public static Bitmap StateBitmap(PresenceState state, int size) => Load(NameFor(state), size).ToBitmap();

    private static readonly Dictionary<string, Bitmap> Flags = new();

    /// <summary>Vlajka jazyka ("en"/"cs"); dimmed = neaktivní (poloprůhledná).</summary>
    public static Bitmap Flag(string lang, bool dimmed)
    {
        lock (Flags)
        {
            var key = lang + (dimmed ? "-dim" : "");
            if (Flags.TryGetValue(key, out var cached)) return cached;
            using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("flag_" + lang + ".png")
                               ?? throw new InvalidOperationException("Chybí vlajka " + lang);
            using var src = new Bitmap(stream);
            var bmp = new Bitmap(src.Width, src.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                var alpha = dimmed ? 0.35f : 1f;
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha });
                g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
            }
            Flags[key] = bmp;
            return bmp;
        }
    }

    private static Icon Load(string name, int size)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((name, size), out var cached)) return cached;
            using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(name + ".ico")
                               ?? throw new InvalidOperationException("Chybí ikona " + name);
            var icon = size > 0 ? new Icon(stream, size, size) : new Icon(stream);
            Cache[(name, size)] = icon;
            return icon;
        }
    }
}

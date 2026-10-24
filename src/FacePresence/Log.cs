namespace FacePresence;

/// <summary>Jednoduchý log do %LOCALAPPDATA%\FacePresence\log.txt (s omezením velikosti).</summary>
internal static class Log
{
    private static readonly object Sync = new();
    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FacePresence");
    public static string FilePath { get; } = Path.Combine(Directory, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000)
                    File.Move(FilePath, Path.Combine(Directory, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // log nesmí nikdy shodit aplikaci
        }
    }
}

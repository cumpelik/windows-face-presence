using FacePresence.Capture;
using FacePresence.Presence;
using FacePresence.Recognition;
using FacePresence.Settings;

namespace FacePresence.App;

/// <summary>Holds the shared application objects (settings, engine, monitor) and the camera selection.</summary>
public sealed class AppHost : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Settings { get; }
    public FaceEngine Engine { get; }
    public ActivityKeeper Keeper { get; }
    public PresenceMonitor Monitor { get; }
    public IReadOnlyList<CameraInfo> Cameras { get; private set; } = Array.Empty<CameraInfo>();

    public event Action? CamerasChanged;

    public AppHost()
    {
        Settings = AppSettings.Load();
        Localization.L.Set(Settings.Language);
        Engine = new FaceEngine();
        Keeper = new ActivityKeeper();
        Monitor = new PresenceMonitor(Engine, Keeper, () => Settings);
    }

    public async Task<IReadOnlyList<CameraInfo>> RefreshCamerasAsync()
    {
        try { Cameras = await CameraEnumerator.ListAsync(); }
        catch (Exception ex)
        {
            Log.Write("Camera enumeration failed: " + ex.Message);
            Cameras = Array.Empty<CameraInfo>();
        }
        Log.Write("Cameras: " + (Cameras.Count == 0 ? "none" : string.Join("; ", Cameras.Select(c => c.DisplayName))));
        CamerasChanged?.Invoke();
        return Cameras;
    }

    /// <summary>The saved camera, otherwise the first one in the list (IR cameras come first).</summary>
    public CameraInfo? PreferredCamera() =>
        Cameras.FirstOrDefault(c => c.SourceId == Settings.CameraSourceId) ?? Cameras.FirstOrDefault();

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await RefreshCamerasAsync();
            var cam = PreferredCamera();
            if (cam == null)
            {
                Monitor.Report(PresenceState.CameraError, new Localization.LocText("host.no_camera"));
                return;
            }
            await Task.Run(() => Monitor.Start(cam));
        }
        finally { _gate.Release(); }
    }

    public async Task SelectCameraAsync(CameraInfo camera)
    {
        await _gate.WaitAsync();
        try
        {
            Settings.CameraSourceId = camera.SourceId;
            Settings.Save();
            await Task.Run(() => Monitor.Start(camera));
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await Task.Run(Monitor.Stop); }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        Monitor.Dispose();
        Keeper.Dispose();
        Engine.Dispose();
    }
}

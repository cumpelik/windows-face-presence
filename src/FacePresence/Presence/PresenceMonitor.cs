using System.Diagnostics;
using FacePresence.Capture;
using FacePresence.Localization;
using FacePresence.Recognition;
using FacePresence.Settings;
using OpenCvSharp;

namespace FacePresence.Presence;

public enum PresenceState
{
    Stopped,
    Starting,
    NotEnrolled,
    Present,
    Absent,
    Paused,
    Locked,
    Enrolling,
    CameraError,
}

public sealed record StatusSnapshot(
    PresenceState State,
    LocText Text,
    double? Score,
    TimeSpan Idle,
    DateTime? NextCheck,
    int Injections,
    DateTime? LastInjection,
    string? CameraFormat)
{
    /// <summary>The status message in the current UI language.</summary>
    public string Message => Text.ToString();
}

public sealed record EnrollResult(bool Success, string Message);

/// <summary>
/// Main loop: frame -> detection/recognition -> keep (or stop keeping) the session active -> adaptive wait.
/// Recognized: next check after PresentIntervalSec, otherwise after AbsentIntervalSec (both configurable).
/// When the session is locked or paused the camera is released (Windows Hello needs it on the lock screen).
/// </summary>
public sealed class PresenceMonitor : IDisposable
{
    private sealed class EnrollJob
    {
        public int Samples;
        public IProgress<string>? Progress;
        public CancellationToken Ct;
        public TaskCompletionSource<EnrollResult> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly FaceEngine _engine;
    private readonly ActivityKeeper _keeper;
    private readonly Func<AppSettings> _settings;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private volatile bool _paused;
    private volatile bool _locked;
    private volatile EnrollJob? _enroll;

    public event Action<StatusSnapshot>? StatusChanged;
    /// <summary>Frame after analysis (raised on the loop thread). The Mat is valid only during the call.</summary>
    public event Action<Mat, FaceResult?, double?>? FrameAnalyzed;

    public StatusSnapshot Status { get; private set; } =
        new(PresenceState.Stopped, new LocText("mon.stopped"), null, TimeSpan.Zero, null, 0, null, null);
    public CameraInfo? Camera { get; private set; }
    public bool IsRunning => _task is { IsCompleted: false };
    public bool IsPaused => _paused;

    public PresenceMonitor(FaceEngine engine, ActivityKeeper keeper, Func<AppSettings> settings)
    {
        _engine = engine;
        _keeper = keeper;
        _settings = settings;
    }

    public void Start(CameraInfo camera)
    {
        Stop();
        Camera = camera;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(() => RunAsync(camera, token), token);
    }

    public void Stop()
    {
        var cts = _cts;
        var task = _task;
        _cts = null;
        _task = null;
        if (cts == null) return;
        cts.Cancel();
        try { task?.Wait(TimeSpan.FromSeconds(8)); } catch { /* cancel */ }
        cts.Dispose();
        _keeper.SetPresent(false);
        Publish(PresenceState.Stopped, T("mon.stopped"));
    }

    public void Report(PresenceState state, LocText message) => Publish(state, message);

    public void SetPaused(bool paused) => _paused = paused;
    public void SetLocked(bool locked) => _locked = locked;

    public Task<EnrollResult> EnrollAsync(int samples, IProgress<string>? progress, CancellationToken ct)
    {
        if (!IsRunning)
            return Task.FromResult(new EnrollResult(false, L.T("mon.not_running")));
        if (_enroll != null)
            return Task.FromResult(new EnrollResult(false, L.T("mon.enroll_busy")));
        var job = new EnrollJob { Samples = samples, Progress = progress, Ct = ct };
        _enroll = job;
        return job.Tcs.Task;
    }

    private static LocText T(string key, params object[] args) => new(key, args);

    private void Publish(PresenceState state, LocText message, double? score = null, DateTime? next = null, string? format = null)
    {
        Status = new StatusSnapshot(state, message, score, ActivityKeeper.GetIdleTime(), next,
            _keeper.InjectionCount, _keeper.LastInjection, format ?? Status.CameraFormat);
        try { StatusChanged?.Invoke(Status); }
        catch (Exception ex) { Log.Write("StatusChanged handler: " + ex.Message); }
    }

    private async Task RunAsync(CameraInfo camera, CancellationToken ct)
    {
        FrameGrabber? grabber = null;
        var grabFailures = 0;
        var openedOnce = false;
        var inCall = false; // another app (e.g. a video call) is using a camera – we step aside
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_locked || _paused)
                {
                    _keeper.SetPresent(false);
                    if (grabber != null) { grabber.Dispose(); grabber = null; }
                    Publish(_locked ? PresenceState.Locked : PresenceState.Paused,
                        _locked ? T("mon.locked") : T("mon.paused"));
                    await Task.Delay(1000, ct);
                    continue;
                }

                // While another app (e.g. a video call) uses a camera, step aside completely: release our camera and stop
                // deferring the lock – Windows (and the call app) behave exactly as without Face Presence. Touching the
                // camera during a call makes the call's picture jump (IR and color are often one USB camera).
                var cameraUsers = CameraUsage.OtherAppsUsingCamera();
                if (cameraUsers.Count > 0)
                {
                    if (!inCall)
                    {
                        Log.Write($"Camera used by {string.Join(", ", cameraUsers)} – checks paused until it stops");
                        inCall = true;
                    }
                    _keeper.SetPresent(false);
                    if (grabber != null) { grabber.Dispose(); grabber = null; }
                    if (_enroll is { } busyJob)
                    {
                        _enroll = null;
                        busyJob.Tcs.TrySetResult(new EnrollResult(false, L.T("enroll.camera_busy")));
                    }
                    Publish(PresenceState.Paused, T("mon.camera_busy", string.Join(", ", cameraUsers)));
                    await Task.Delay(2000, ct);
                    continue;
                }
                if (inCall)
                {
                    Log.Write("No other app uses a camera any more – checks resumed");
                    inCall = false;
                }

                if (grabber == null)
                {
                    // The camera is reopened for every check while the user is present (see below); only the first
                    // open is reported and logged.
                    if (!openedOnce) Publish(PresenceState.Starting, T("mon.opening"));
                    try
                    {
                        grabber = await FrameGrabber.OpenAsync(camera, ct, log: !openedOnce);
                        grabFailures = 0;
                        if (!openedOnce) Publish(PresenceState.Starting, T("mon.opened"), format: grabber.FormatDescription);
                        openedOnce = true;
                        await Task.Delay(400, ct); // let exposure (and the IR illuminator) settle
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Log.Write("Opening the camera failed: " + ex.Message);
                        _keeper.SetPresent(false);
                        Publish(PresenceState.CameraError, T("mon.camera_error", ex.Message));
                        await Task.Delay(5000, ct);
                        continue;
                    }
                }

                var job = _enroll;
                if (job != null)
                {
                    _keeper.SetPresent(false);
                    await RunEnrollAsync(grabber, job, ct);
                    _enroll = null;
                    continue;
                }

                Mat? frame = null;
                try { frame = await grabber.GrabAsync(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Write("Grab failed: " + ex.Message); }

                if (frame == null)
                {
                    grabFailures++;
                    _keeper.SetPresent(false);
                    Publish(PresenceState.CameraError, T("mon.no_frames", grabFailures));
                    if (grabFailures >= 3)
                    {
                        grabber.Dispose();
                        grabber = null;
                    }
                    await Task.Delay(1000, ct);
                    continue;
                }
                grabFailures = 0;

                var settings = _settings();
                TimeSpan delay;
                FaceEngine.Downscale(ref frame);
                using (frame)
                {
                    FaceResult? face = null;
                    try { face = _engine.Analyze(frame); }
                    catch (Exception ex) { Log.Write("Analyze selhal: " + ex.Message); }

                    // Compare only with samples from the same camera type (IR vs. color) – otherwise the score is meaningless.
                    var templates = settings.Templates.Where(t => t.CameraKind == camera.Kind).ToList();
                    double? score = face != null && templates.Count > 0
                        ? templates.Max(t => FaceEngine.Cosine(t.Feature, face.Feature))
                        : null;
                    var present = score.HasValue && score.Value >= settings.MatchThreshold;
                    var state = templates.Count == 0 ? PresenceState.NotEnrolled
                        : present ? PresenceState.Present : PresenceState.Absent;

                    _keeper.SetPresent(present);
                    var injected = false;
                    var idleThreshold = EffectiveIdleThreshold(settings);
                    if (present && settings.KeepAliveEnabled)
                        injected = _keeper.InjectIfIdle(idleThreshold, settings.KeepAliveMethod);

                    try { FrameAnalyzed?.Invoke(frame, face, score); }
                    catch (Exception ex) { Log.Write("FrameAnalyzed handler: " + ex.Message); }

                    var interval = present ? settings.PresentIntervalSec : settings.AbsentIntervalSec;
                    delay = TimeSpan.FromSeconds(Math.Max(0.2, interval));
                    if (present && settings.KeepAliveEnabled)
                    {
                        // Wake up earlier if the real idle time would otherwise exceed the threshold (input is only ever sent after a fresh face check).
                        var untilInject = idleThreshold - ActivityKeeper.GetIdleTime();
                        if (untilInject < delay)
                            delay = untilInject < TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : untilInject;
                    }

                    var msg = state switch
                    {
                        PresenceState.NotEnrolled => T(face == null ? "mon.not_enrolled_nobody" : "mon.not_enrolled_face"),
                        PresenceState.Present => T(injected ? "mon.present_injected" : "mon.present"),
                        _ => T(face == null ? "mon.nobody" : "mon.other"),
                    };
                    Publish(state, msg, score, DateTime.Now + delay);
                }

                // Release the camera between checks that are far apart, so the IR illuminator does not blink all the
                // time; for the fast checks (user not recognized) it stays open.
                if (delay >= TimeSpan.FromSeconds(3) && _enroll == null)
                {
                    grabber.Dispose();
                    grabber = null;
                }

                await Task.Delay(delay, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            Log.Write("Monitoring loop crashed: " + ex);
            Publish(PresenceState.CameraError, T("mon.camera_error", ex.Message));
        }
        finally
        {
            _keeper.SetPresent(false);
            grabber?.Dispose();
            var job = _enroll;
            _enroll = null;
            job?.Tcs.TrySetResult(new EnrollResult(false, L.T("mon.enroll_stopped")));
        }
    }

    /// <summary>Idle threshold for simulated input: the configured value, but at most 60 % of the detected lock timeout.</summary>
    private static TimeSpan EffectiveIdleThreshold(AppSettings settings)
    {
        var threshold = TimeSpan.FromSeconds(settings.KeepAliveIdleSec);
        var rule = LockPolicy.Get();
        if (rule != null)
        {
            var safe = TimeSpan.FromSeconds(Math.Max(5, rule.Timeout.TotalSeconds * 0.6));
            if (safe < threshold) threshold = safe;
        }
        return threshold;
    }

    private async Task RunEnrollAsync(FrameGrabber grabber, EnrollJob job, CancellationToken ct)
    {
        var features = new List<float[]>();
        var sw = Stopwatch.StartNew();
        try
        {
            Publish(PresenceState.Enrolling, T("mon.enrolling"));
            while (features.Count < job.Samples && sw.Elapsed < TimeSpan.FromSeconds(40)
                   && !job.Ct.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                var frame = await grabber.GrabAsync(TimeSpan.FromSeconds(2), ct);
                if (frame == null) continue;
                FaceEngine.Downscale(ref frame);
                using (frame)
                {
                    var face = _engine.Analyze(frame);
                    var ok = face != null && face.Score >= 0.85f && face.Box.Width >= 40;
                    if (ok)
                    {
                        features.Add(face!.Feature);
                        job.Progress?.Report(L.T("enroll.sample", features.Count, job.Samples));
                    }
                    else
                    {
                        job.Progress?.Report(L.T(face == null ? "enroll.no_face" : "enroll.small"));
                    }
                    try { FrameAnalyzed?.Invoke(frame, face, null); } catch { }
                }
                await Task.Delay(350, ct);
            }

            if (features.Count < Math.Max(3, job.Samples / 2))
            {
                job.Tcs.TrySetResult(new EnrollResult(false, L.T("enroll.too_few", features.Count)));
                return;
            }

            var minSim = 1.0;
            for (var i = 0; i < features.Count; i++)
                for (var j = i + 1; j < features.Count; j++)
                    minSim = Math.Min(minSim, FaceEngine.Cosine(features[i], features[j]));

            var kind = Camera?.Kind ?? "Color";
            var settings = _settings();
            var kept = settings.Templates.Where(t => t.CameraKind != kind).ToList();
            kept.AddRange(features.Select(f => new FaceTemplate { CameraKind = kind, Feature = f, Created = DateTime.Now }));
            settings.Templates = kept;
            settings.Save();
            Log.Write($"Enrollment: {features.Count} samples ({kind}), min. mutual similarity {minSim:F3}");
            job.Tcs.TrySetResult(new EnrollResult(true,
                L.T("enroll.saved", features.Count, kind, minSim) + (minSim < 0.5 ? L.T("enroll.low") : ".")));
        }
        catch (OperationCanceledException)
        {
            job.Tcs.TrySetResult(new EnrollResult(false, L.T("enroll.cancelled")));
            throw;
        }
        catch (Exception ex)
        {
            Log.Write("Registrace selhala: " + ex);
            job.Tcs.TrySetResult(new EnrollResult(false, L.T("enroll.failed", ex.Message)));
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

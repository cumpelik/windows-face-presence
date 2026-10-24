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
    string Message,
    double? Score,
    TimeSpan Idle,
    DateTime? NextCheck,
    int Injections,
    DateTime? LastInjection,
    string? CameraFormat);

public sealed record EnrollResult(bool Success, string Message);

/// <summary>
/// Hlavní smyčka: snímek -> detekce/rozpoznání -> (ne)udržovat aktivitu -> adaptivní čekání.
/// Rozpoznán: další kontrola za PresentIntervalSec, jinak za AbsentIntervalSec (obě nastavitelné).
/// Při zamčené session nebo pauze kameru uvolní (Windows Hello ji na lock screenu potřebuje).
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
    /// <summary>Snímek po analýze (vlákno smyčky). Mat platí jen po dobu volání.</summary>
    public event Action<Mat, FaceResult?, double?>? FrameAnalyzed;

    public StatusSnapshot Status { get; private set; } =
        new(PresenceState.Stopped, "", null, TimeSpan.Zero, null, 0, null, null);
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
        Publish(PresenceState.Stopped, L.T("mon.stopped"));
    }

    public void Report(PresenceState state, string message) => Publish(state, message);

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

    private void Publish(PresenceState state, string message, double? score = null, DateTime? next = null, string? format = null)
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
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_locked || _paused)
                {
                    _keeper.SetPresent(false);
                    if (grabber != null) { grabber.Dispose(); grabber = null; }
                    Publish(_locked ? PresenceState.Locked : PresenceState.Paused,
                        _locked ? L.T("mon.locked") : L.T("mon.paused"));
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (grabber == null)
                {
                    Publish(PresenceState.Starting, L.T("mon.opening"));
                    try
                    {
                        grabber = await FrameGrabber.OpenAsync(camera, ct);
                        grabFailures = 0;
                        Publish(PresenceState.Starting, L.T("mon.opened"), format: grabber.FormatDescription);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Log.Write("Otevření kamery selhalo: " + ex.Message);
                        _keeper.SetPresent(false);
                        Publish(PresenceState.CameraError, L.T("mon.camera_error", ex.Message));
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
                catch (Exception ex) { Log.Write("Grab selhal: " + ex.Message); }

                if (frame == null)
                {
                    grabFailures++;
                    _keeper.SetPresent(false);
                    Publish(PresenceState.CameraError, L.T("mon.no_frames", grabFailures));
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

                    // Šablony se porovnávají jen se vzorky z téhož typu kamery (IR vs. barevná) – jinak je skóre nesmyslné.
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
                        // Probudit se dřív, pokud by jinak skutečná nečinnost přerostla práh (vstup se posílá vždy až po novém ověření obličeje).
                        var untilInject = idleThreshold - ActivityKeeper.GetIdleTime();
                        if (untilInject < delay)
                            delay = untilInject < TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : untilInject;
                    }

                    var msg = state switch
                    {
                        PresenceState.NotEnrolled => L.T(face == null ? "mon.not_enrolled_nobody" : "mon.not_enrolled_face"),
                        PresenceState.Present => L.T(injected ? "mon.present_injected" : "mon.present"),
                        _ => L.T(face == null ? "mon.nobody" : "mon.other"),
                    };
                    Publish(state, msg, score, DateTime.Now + delay);
                }

                await Task.Delay(delay, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normální ukončení
        }
        catch (Exception ex)
        {
            Log.Write("Smyčka sledování spadla: " + ex);
            Publish(PresenceState.CameraError, L.T("mon.camera_error", ex.Message));
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

    /// <summary>Práh nečinnosti pro simulovaný vstup: nastavená hodnota, ale nejvýš 60 % zjištěného limitu zámku.</summary>
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
            Publish(PresenceState.Enrolling, L.T("mon.enrolling"));
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
            Log.Write($"Registrace: {features.Count} vzorků ({kind}), min. vzájemná podobnost {minSim:F3}");
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

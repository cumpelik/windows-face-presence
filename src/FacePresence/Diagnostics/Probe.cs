using System.Runtime.InteropServices;
using System.Text;
using FacePresence.App;
using FacePresence.Capture;
using FacePresence.Localization;
using FacePresence.Native;
using FacePresence.Presence;
using FacePresence.Recognition;
using Microsoft.Win32;
using OpenCvSharp;
using Windows.Media.Capture.Frames;

namespace FacePresence.Diagnostics;

/// <summary>
/// Verifies the two critical points directly on the target machine:
///  1) can we get frames from the Windows Hello IR camera (and is a face found in them)?
///  2) does the simulated input reset the real idle counter (GetLastInputInfo), and which lock policies apply?
/// </summary>
public static class Probe
{
    public static async Task<string> RunAsync(AppHost host, IProgress<string> progress, CancellationToken ct)
    {
        var sb = new StringBuilder();
        void W(string line = "")
        {
            sb.AppendLine(line);
            progress.Report(line);
        }

        W(L.T("p.header", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        W($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, Face Presence {typeof(Probe).Assembly.GetName().Version?.ToString(3)}");
        W($"EXE: {Environment.ProcessPath}");
        W();

        var irOk = false;
        var colorOk = false;
        var irFace = false;

        // ---------- 1) cameras ----------
        W(L.T("p.cameras"));
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            if (groups.Count == 0) W(L.T("p.no_groups"));
            foreach (var g in groups)
            {
                W(L.T("p.group", g.DisplayName));
                W($"    Id: {g.Id}");
                foreach (var si in g.SourceInfos)
                    W($"    - {si.SourceKind} / {si.MediaStreamType}  (device: {si.DeviceInformation?.Name ?? "?"})");
            }
        }
        catch (Exception ex)
        {
            W(L.T("p.enum_error", ex.Message));
        }
        W();

        var users = CameraUsage.OtherAppsUsingCamera();
        W(L.T("p.camera_users", users.Count == 0 ? L.T("p.none") : string.Join(", ", users)));
        W();

        var cams = await CameraEnumerator.ListAsync();
        var toTest = cams.Where(c => c.IsInfrared).Concat(cams.Where(c => !c.IsInfrared).Take(1)).ToList();
        if (toTest.Count == 0) W(L.T("p.no_usable"));

        foreach (var cam in toTest)
        {
            ct.ThrowIfCancellationRequested();
            W(L.T("p.test_camera", cam.DisplayName));
            FrameGrabber? grabber = null;
            try
            {
                grabber = await FrameGrabber.OpenAsync(cam, ct);
                W(L.T("p.opened", grabber.FormatDescription, grabber.SharingMode));
                W(L.T("p.formats", string.Join(", ", grabber.SupportedFormats.Take(12)) + (grabber.SupportedFormats.Count > 12 ? ", …" : "")));

                Mat? last = null;
                var got = 0;
                var meanSum = 0.0;
                var start = DateTime.UtcNow;
                while (got < 10 && DateTime.UtcNow - start < TimeSpan.FromSeconds(4))
                {
                    var f = await grabber.GrabAsync(TimeSpan.FromSeconds(1), ct);
                    if (f == null) continue;
                    got++;
                    meanSum += Cv2.Mean(f).Val0;
                    last?.Dispose();
                    last = f;
                }
                W(L.T("p.frames", got, grabber.IlluminatedFrames, grabber.DarkFrames, grabber.LastPixelFormat ?? "?"));
                if (got > 0 && last != null)
                {
                    W(L.T("p.size", last.Width, last.Height, meanSum / got));
                    var path = Path.Combine(Log.Directory, $"probe_{cam.Kind}.png");
                    try { Cv2.ImWrite(path, last); W(L.T("p.sample_saved", path)); }
                    catch (Exception ex) { W(L.T("p.sample_failed", ex.Message)); }

                    FaceEngine.Downscale(ref last);
                    var face = host.Engine.Analyze(last);
                    if (face != null)
                    {
                        W(L.T("p.face_found", face.Box.Width, face.Box.Height, face.Score, face.Feature.Length));
                        if (cam.IsInfrared) irFace = true;
                    }
                    else W(L.T("p.face_none"));

                    if (cam.IsInfrared) irOk = true; else colorOk = true;
                }
                else W(L.T("p.no_frames"));
                last?.Dispose();
            }
            catch (Exception ex)
            {
                W(L.T("p.cam_error", ex.Message));
            }
            finally
            {
                grabber?.Dispose();
            }
        }
        W();

        // ---------- 2) lock policies ----------
        W(L.T("p.policies"));
        var inactivity = LockPolicy.ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs");
        W(L.T("p.inactivity", inactivity is null or 0 ? L.T("p.not_set") : inactivity + " s"));
        W(L.T("p.inactivity_note"));

        foreach (var (key, labelKey) in new[]
                 {
                     (@"Software\Policies\Microsoft\Windows\Control Panel\Desktop", "p.ss_policy"),
                     (@"Control Panel\Desktop", "p.ss_user"),
                 })
        {
            var active = LockPolicy.ReadString(Registry.CurrentUser, key, "ScreenSaveActive");
            var timeout = LockPolicy.ReadString(Registry.CurrentUser, key, "ScreenSaveTimeOut");
            var secure = LockPolicy.ReadString(Registry.CurrentUser, key, "ScreenSaverIsSecure");
            var exe = LockPolicy.ReadString(Registry.CurrentUser, key, "SCRNSAVE.EXE");
            if (active == null && timeout == null && secure == null && exe == null) W(L.T("p.ss_none", L.T(labelKey)));
            else W(L.T("p.ss", L.T(labelKey), active ?? "-", timeout ?? "-", secure ?? "-", exe ?? "-"));
        }

        var goodbye = LockPolicy.ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "EnableGoodbye");
        W(L.T(goodbye == 1 ? "p.goodbye_on" : "p.goodbye_off"));
        W(L.T("p.modern_standby", L.T(LockPolicy.IsModernStandby() ? "p.yes" : "p.no")));

        try
        {
            var (videoAc, videoDc) = LockPolicy.ReadPower(NativeMethods.GUID_VIDEO_SUBGROUP, NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT);
            var (sleepAc, sleepDc) = LockPolicy.ReadPower(NativeMethods.GUID_SLEEP_SUBGROUP, NativeMethods.GUID_STANDBY_TIMEOUT);
            var (lockAc, lockDc) = LockPolicy.ReadPower(NativeMethods.GUID_NO_SUBGROUP, NativeMethods.GUID_LOCK_CONSOLE_ON_WAKE);
            var delay = LockPolicy.ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DelayLockInterval");
            W(L.T("p.display", Fmt(videoAc), Fmt(videoDc)));
            W(L.T("p.sleep", Fmt(sleepAc), Fmt(sleepDc)));
            W(L.T("p.require", L.T(lockAc == 1 ? "p.yes" : "p.no"), L.T(lockDc == 1 ? "p.yes" : "p.no")));
            W(L.T("p.delaylock", delay is null ? L.T("p.not_set") : delay + " s"));
            W(L.T("p.power_note"));
        }
        catch (Exception ex)
        {
            W(L.T("p.power_error", ex.Message));
        }
        var rule = LockPolicy.Evaluate();
        W(L.T("p.effective", rule == null ? L.T("p.effective_none") : L.T("p.effective_val", L.Duration(rule.Timeout), rule.SourceName)));
        W();

        // ---------- 3) simulated input ----------
        W(L.T("p.keepalive"));
        var prev = NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS | NativeMethods.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
        W(L.T("p.ste", L.T(prev != 0 ? "p.ok" : "p.failed")));
        NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);

        var injectOk = false;
        foreach (var method in new[] { host.Settings.KeepAliveMethod, KeepAliveMethod.ZeroMouseMove, KeepAliveMethod.MouseJiggle }.Distinct())
        {
            await Task.Delay(1500, ct);
            var before = ActivityKeeper.GetIdleTime();
            var sent = ActivityKeeper.Inject(method);
            await Task.Delay(150, ct);
            var after = ActivityKeeper.GetIdleTime();
            var reset = sent && after < TimeSpan.FromMilliseconds(600) && after < before;
            W(L.T("p.inject", method, L.T(sent ? "p.ok" : "p.failed"), before.TotalMilliseconds, after.TotalMilliseconds, L.T(reset ? "p.resets" : "p.noreset")));
            if (reset && !injectOk && method != host.Settings.KeepAliveMethod) W(L.T("p.switch", method));
            injectOk |= reset;
        }
        W();

        // ---------- summary ----------
        W(L.T("p.summary"));
        W(irOk ? L.T("p.sum_ir_ok", L.T(irFace ? "p.sum_ir_face" : "p.sum_ir_noface"))
            : cams.Any(c => c.IsInfrared) ? L.T("p.sum_ir_fail") : L.T("p.sum_ir_none"));
        W(L.T(colorOk ? "p.sum_color_ok" : "p.sum_color_fail"));
        W(L.T(injectOk ? "p.sum_keep_ok" : "p.sum_keep_fail"));
        if (rule != null)
            W(L.T("p.sum_limit", L.Duration(rule.Timeout), rule.SourceName, host.Settings.KeepAliveIdleSec));

        var text = sb.ToString();
        try { File.WriteAllText(Path.Combine(Log.Directory, "probe.txt"), text); } catch { }
        return text;
    }

    private static string Fmt(uint? seconds) => seconds switch { null => "?", 0 => L.T("p.never"), _ => seconds + " s" };
}

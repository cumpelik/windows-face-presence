using System.Diagnostics;
using OpenCvSharp;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace FacePresence.Capture;

/// <summary>
/// Opens a camera (IR or color) through WinRT MediaCapture and returns the current frame as a Mat (BGR) on request.
/// For IR cameras illuminated frames are preferred (InfraredMediaFrame.IsIlluminated).
/// </summary>
public sealed class FrameGrabber : IDisposable
{
    private readonly MediaCapture _capture;
    private readonly MediaFrameReader _reader;
    private readonly CameraInfo _camera;
    private TimeSpan? _lastTimestamp;
    private bool _disposed;
    private bool _log = true;

    public CameraInfo Camera => _camera;
    public string FormatDescription { get; }
    public string SharingMode { get; }
    public int IlluminatedFrames { get; private set; }
    public int DarkFrames { get; private set; }
    public string? LastPixelFormat { get; private set; }
    public IReadOnlyList<string> SupportedFormats { get; private set; } = Array.Empty<string>();

    private FrameGrabber(MediaCapture capture, MediaFrameReader reader, CameraInfo camera, string format, string sharing)
    {
        _capture = capture;
        _reader = reader;
        _camera = camera;
        FormatDescription = format;
        SharingMode = sharing;
    }

    public static async Task<FrameGrabber> OpenAsync(CameraInfo camera, CancellationToken ct = default, bool log = true)
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var group = groups.FirstOrDefault(g => g.Id == camera.GroupId)
                    ?? throw new InvalidOperationException("Camera is not connected: " + camera.DisplayName);

        MediaCapture? capture = null;
        Exception? lastError = null;
        var sharing = "";
        foreach (var mode in new[] { MediaCaptureSharingMode.ExclusiveControl, MediaCaptureSharingMode.SharedReadOnly })
        {
            ct.ThrowIfCancellationRequested();
            var mc = new MediaCapture();
            try
            {
                await mc.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    SourceGroup = group,
                    SharingMode = mode,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                });
                capture = mc;
                sharing = mode.ToString();
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                mc.Dispose();
            }
        }
        if (capture == null)
            throw new InvalidOperationException("Cannot open the camera: " + lastError?.Message, lastError);

        try
        {
            if (!capture.FrameSources.TryGetValue(camera.SourceId, out var source))
                throw new InvalidOperationException("Frame source not found: " + camera.SourceId);

            var format = PickFormat(source.SupportedFormats, camera.IsInfrared);
            if (format != null)
            {
                try { await source.SetFormatAsync(format); }
                catch (Exception ex) { Log.Write("SetFormatAsync failed (continuing with the default format): " + ex.Message); }
            }

            var reader = camera.IsInfrared
                ? await capture.CreateFrameReaderAsync(source)
                : await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Argb32);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                reader.Dispose();
                throw new InvalidOperationException("Cannot start reading frames: " + status);
            }

            var cur = source.CurrentFormat;
            var desc = $"{cur.Subtype} {cur.VideoFormat.Width}x{cur.VideoFormat.Height} @ {cur.FrameRate.Numerator / Math.Max(1, cur.FrameRate.Denominator)} fps";
            if (log) Log.Write($"Camera opened: {camera.DisplayName} {desc} ({sharing})");
            var all = source.SupportedFormats
                .Select(f => $"{f.Subtype} {f.VideoFormat?.Width}x{f.VideoFormat?.Height}@{f.FrameRate.Numerator / Math.Max(1, f.FrameRate.Denominator)}")
                .Distinct().ToList();
            return new FrameGrabber(capture, reader, camera, desc, sharing) { SupportedFormats = all, _log = log };
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    private static MediaFrameFormat? PickFormat(IReadOnlyList<MediaFrameFormat> formats, bool infrared)
    {
        if (formats.Count == 0) return null;
        static int Rank(string subtype) => subtype.ToUpperInvariant() switch
        {
            "NV12" => 0, "YUY2" => 1, "L8" => 0, "L16" => 1, "RGB32" => 2, "ARGB32" => 2, "MJPG" => 5, _ => 3,
        };
        return formats
            .Where(f => f.VideoFormat != null && f.VideoFormat.Width >= 160 && f.VideoFormat.Width <= 1280)
            .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - 640))
            .ThenBy(f => Rank(f.Subtype))
            .FirstOrDefault() ?? formats[0];
    }

    /// <summary>Returns a new (not yet returned) frame, or null after the timeout. The caller owns the Mat.</summary>
    public async Task<Mat?> GrabAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (_disposed) return null;
        var sw = Stopwatch.StartNew();
        Mat? dark = null;
        try
        {
            while (sw.Elapsed < timeout)
            {
                ct.ThrowIfCancellationRequested();
                using var frame = _reader.TryAcquireLatestFrame();
                var video = frame?.VideoMediaFrame;
                var ts = frame?.SystemRelativeTime;
                if (frame == null || video == null || ts == null || ts == _lastTimestamp)
                {
                    await Task.Delay(15, ct);
                    continue;
                }
                _lastTimestamp = ts;

                var bitmap = video.SoftwareBitmap;
                if (bitmap == null)
                {
                    await Task.Delay(15, ct);
                    continue;
                }
                LastPixelFormat = bitmap.BitmapPixelFormat.ToString();

                bool? illuminated = null;
                if (_camera.IsInfrared)
                {
                    try { illuminated = video.InfraredMediaFrame?.IsIlluminated; }
                    catch { illuminated = null; }
                }

                var mat = FrameConverter.ToBgr(bitmap);
                if (illuminated == false)
                {
                    DarkFrames++;
                    dark?.Dispose();
                    dark = mat;
                    // IR cameras alternate illuminated and dark frames; wait a little for an illuminated one.
                    if (sw.Elapsed > TimeSpan.FromMilliseconds(700))
                    {
                        var r = dark; dark = null; return r;
                    }
                    await Task.Delay(10, ct);
                    continue;
                }
                if (illuminated == true) IlluminatedFrames++;
                dark?.Dispose(); dark = null;
                return mat;
            }
            var last = dark; dark = null;
            return last;
        }
        finally
        {
            dark?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _reader.StopAsync().AsTask().Wait(2000); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _capture.Dispose(); } catch { }
        if (_log) Log.Write("Camera released: " + _camera.DisplayName);
    }
}

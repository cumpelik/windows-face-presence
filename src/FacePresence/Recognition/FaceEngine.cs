using OpenCvSharp;
using OpenCvSharp.Dnn;
using CvSize = OpenCvSharp.Size;
using CvRect = OpenCvSharp.Rect;

namespace FacePresence.Recognition;

public sealed record FaceResult(CvRect Box, float Score, float[] Feature);

/// <summary>
/// Local face detection (YuNet via cv::FaceDetectorYN) + face embedding (SFace via cv::dnn), both models from the
/// OpenCV Zoo, no cloud. Comparison = cosine similarity of 128-D vectors.
/// Face alignment (5 points -> 112x112) re-implements cv::FaceRecognizerSF::alignCrop, because the
/// OpenCvSharp 4.13 NuGet package does not wrap that class yet.
/// </summary>
public sealed class FaceEngine : IDisposable
{
    private FaceDetectorYN _detector;
    private readonly string _yunetPath;
    private CvSize _detectorSize = new(320, 320);
    private readonly Net _recognizer;
    private readonly object _sync = new();

    public const int MaxWidth = 640;

    // Reference positions of the 5 landmarks (right eye, left eye, nose, right and left mouth corner) in 112x112 – same as OpenCV.
    private static readonly double[,] RefPoints =
    {
        { 38.2946, 51.6963 }, { 73.5318, 51.5014 }, { 56.0252, 71.7366 }, { 41.5493, 92.3655 }, { 70.7299, 92.2041 },
    };

    public FaceEngine()
    {
        _yunetPath = ExtractModel("yunet.onnx");
        _detector = CreateDetector(_yunetPath, _detectorSize);
        _recognizer = CvDnn.ReadNetFromOnnx(LoadResource("sface.onnx"))
                      ?? throw new InvalidOperationException("Failed to load the SFace model");
        _recognizer.SetPreferableBackend(Backend.OPENCV);
        _recognizer.SetPreferableTarget(Target.CPU);
    }

    private static FaceDetectorYN CreateDetector(string path, CvSize size) =>
        FaceDetectorYN.Create(path, "", size, scoreThreshold: 0.7f, nmsThreshold: 0.3f, topK: 50);

    private static byte[] LoadResource(string name)
    {
        using var stream = typeof(FaceEngine).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException("Missing embedded model " + name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>This wrapper version of FaceDetectorYN can load the model from a path only – extract it from the EXE to LocalAppData.</summary>
    private static string ExtractModel(string name)
    {
        var dir = Path.Combine(Log.Directory, "models");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        var bytes = LoadResource(name);
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
            File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Downscales the frame to a reasonable width (replaces the Mat) for speed.</summary>
    public static void Downscale(ref Mat bgr)
    {
        if (bgr.Width <= MaxWidth) return;
        var scale = (double)MaxWidth / bgr.Width;
        var small = new Mat();
        Cv2.Resize(bgr, small, new CvSize(), scale, scale, InterpolationFlags.Area);
        bgr.Dispose();
        bgr = small;
    }

    /// <summary>Finds the largest face and computes its embedding. Null when there is no face.</summary>
    public FaceResult? Analyze(Mat bgr)
    {
        lock (_sync)
        {
            if (_detectorSize.Width != bgr.Width || _detectorSize.Height != bgr.Height)
            {
                // YuNet needs a matching input size; wrapper 4.13 has no SetInputSize, so the detector is recreated (the camera size rarely changes).
                _detectorSize = new CvSize(bgr.Width, bgr.Height);
                _detector.Dispose();
                _detector = CreateDetector(_yunetPath, _detectorSize);
            }
            using var faces = new Mat();
            _detector.Detect(bgr, faces);
            if (faces.Empty() || faces.Rows == 0) return null;

            var best = -1;
            var bestArea = 0f;
            var rows = faces.Rows;
            for (var i = 0; i < rows; i++)
            {
                var area = faces.Get<float>(i, 2) * faces.Get<float>(i, 3);
                if (area > bestArea) { bestArea = area; best = i; }
            }

            // row: x, y, w, h, 5x (lx, ly), score
            var landmarks = new double[5, 2];
            for (var k = 0; k < 5; k++)
            {
                landmarks[k, 0] = faces.Get<float>(best, 4 + k * 2);
                landmarks[k, 1] = faces.Get<float>(best, 5 + k * 2);
            }

            using var warp = SimilarityTransform(landmarks);
            using var aligned = new Mat();
            Cv2.WarpAffine(bgr, aligned, warp, new CvSize(112, 112), InterpolationFlags.Linear);

            using var blob = CvDnn.BlobFromImage(aligned, 1.0, new CvSize(112, 112), new Scalar(0, 0, 0), swapRB: true, crop: false);
            _recognizer.SetInput(blob);
            using var output = _recognizer.Forward();
            var vec = new float[output.Total()];
            for (var i = 0; i < vec.Length; i++) vec[i] = output.Get<float>(0, i);

            var box = new CvRect(
                (int)faces.Get<float>(best, 0), (int)faces.Get<float>(best, 1),
                (int)faces.Get<float>(best, 2), (int)faces.Get<float>(best, 3));
            return new FaceResult(box, faces.Get<float>(best, 14), vec);
        }
    }

    /// <summary>Umeyama – similarity transform (rotation, scale, translation) from the 5 landmarks to the reference points; 2x3 CV_64F.</summary>
    private static Mat SimilarityTransform(double[,] src)
    {
        const int n = 5;
        double sx = 0, sy = 0, dx = 0, dy = 0;
        for (var i = 0; i < n; i++) { sx += src[i, 0]; sy += src[i, 1]; dx += RefPoints[i, 0]; dy += RefPoints[i, 1]; }
        sx /= n; sy /= n; dx /= n; dy /= n;

        double a00 = 0, a01 = 0, a10 = 0, a11 = 0, var1 = 0, var2 = 0;
        for (var i = 0; i < n; i++)
        {
            var sdx = src[i, 0] - sx; var sdy = src[i, 1] - sy;
            var ddx = RefPoints[i, 0] - dx; var ddy = RefPoints[i, 1] - dy;
            a00 += ddx * sdx; a01 += ddx * sdy; a10 += ddy * sdx; a11 += ddy * sdy;
            var1 += sdx * sdx; var2 += sdy * sdy;
        }
        a00 /= n; a01 /= n; a10 /= n; a11 /= n; var1 /= n; var2 /= n;

        using var a = new Mat(2, 2, MatType.CV_64FC1);
        a.Set(0, 0, a00); a.Set(0, 1, a01); a.Set(1, 0, a10); a.Set(1, 1, a11);
        var d = new[] { 1.0, 1.0 };
        if (Cv2.Determinant(a) < 0) d[1] = -1;

        using var w = new Mat();
        using var u = new Mat();
        using var vt = new Mat();
        Cv2.SVDecomp(a, w, u, vt, SVD.Flags.FullUV);

        // R = U * diag(d) * Vt (2x2, by hand – no temporary MatExpr)
        var r = new double[2, 2];
        for (var i = 0; i < 2; i++)
            for (var j = 0; j < 2; j++)
                r[i, j] = u.Get<double>(i, 0) * d[0] * vt.Get<double>(0, j) + u.Get<double>(i, 1) * d[1] * vt.Get<double>(1, j);

        var scale = (var1 + var2) > 0 ? (w.Get<double>(0, 0) * d[0] + w.Get<double>(1, 0) * d[1]) / (var1 + var2) : 1.0;

        var t = new Mat(2, 3, MatType.CV_64FC1);
        for (var i = 0; i < 2; i++)
            for (var j = 0; j < 2; j++)
                t.Set(i, j, scale * r[i, j]);
        t.Set(0, 2, dx - (t.Get<double>(0, 0) * sx + t.Get<double>(0, 1) * sy));
        t.Set(1, 2, dy - (t.Get<double>(1, 0) * sx + t.Get<double>(1, 1) * sy));
        return t;
    }

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return -1;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return -1;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public void Dispose()
    {
        _detector.Dispose();
        _recognizer.Dispose();
    }
}

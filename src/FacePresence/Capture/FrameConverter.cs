using System.Runtime.InteropServices;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using WinRT;

namespace FacePresence.Capture;

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

/// <summary>Převod WinRT SoftwareBitmap -> OpenCV Mat (BGR, 8 bit). Výsledek je vlastní kopie.</summary>
public static class FrameConverter
{
    public static unsafe Mat ToBgr(SoftwareBitmap bitmap)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        reference.As<IMemoryBufferByteAccess>().GetBuffer(out var data, out _);
        var plane = buffer.GetPlaneDescription(0);
        var ptr = (IntPtr)(data + plane.StartIndex);

        switch (bitmap.BitmapPixelFormat)
        {
            case BitmapPixelFormat.Gray8:
            {
                using var gray = Mat.FromPixelData(plane.Height, plane.Width, MatType.CV_8UC1, ptr, plane.Stride);
                return GrayToBgr(gray);
            }
            case BitmapPixelFormat.Gray16:
            {
                using var g16 = Mat.FromPixelData(plane.Height, plane.Width, MatType.CV_16UC1, ptr, plane.Stride);
                using var g8 = new Mat();
                Cv2.Normalize(g16, g8, 0, 255, NormTypes.MinMax, MatType.CV_8U);
                return GrayToBgr(g8);
            }
            case BitmapPixelFormat.Nv12:
            {
                // Y rovina = jas; chroma ignorujeme (u IR stejně není).
                using var gray = Mat.FromPixelData(plane.Height, plane.Width, MatType.CV_8UC1, ptr, plane.Stride);
                return GrayToBgr(gray);
            }
            case BitmapPixelFormat.Bgra8:
            {
                using var bgra = Mat.FromPixelData(plane.Height, plane.Width, MatType.CV_8UC4, ptr, plane.Stride);
                var bgr = new Mat();
                Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
                return bgr;
            }
            case BitmapPixelFormat.Rgba8:
            {
                using var rgba = Mat.FromPixelData(plane.Height, plane.Width, MatType.CV_8UC4, ptr, plane.Stride);
                var bgr = new Mat();
                Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
                return bgr;
            }
            case BitmapPixelFormat.Yuy2:
            {
                using var yuy2 = Mat.FromPixelData(plane.Height, plane.Width, MatType.CV_8UC2, ptr, plane.Stride);
                var bgr = new Mat();
                Cv2.CvtColor(yuy2, bgr, ColorConversionCodes.YUV2BGR_YUY2);
                return bgr;
            }
            default:
            {
                using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
                return ToBgr(converted);
            }
        }
    }

    private static Mat GrayToBgr(Mat gray)
    {
        var bgr = new Mat();
        Cv2.CvtColor(gray, bgr, ColorConversionCodes.GRAY2BGR);
        return bgr;
    }
}

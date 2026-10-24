using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

namespace FacePresence.Capture;

/// <summary>
/// Camera enumeration through WinRT MediaFrameSourceGroup. Unlike DirectShow / OpenCV VideoCapture it also
/// sees the Windows Hello sensor (IR) cameras (KSCATEGORY_SENSOR_CAMERA).
/// </summary>
public static class CameraEnumerator
{
    public static async Task<List<CameraInfo>> ListAsync()
    {
        var result = new List<CameraInfo>();
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        foreach (var group in groups)
        {
            foreach (var info in group.SourceInfos)
            {
                if (info.MediaStreamType is not (MediaStreamType.VideoPreview or MediaStreamType.VideoRecord))
                    continue;
                if (info.SourceKind is not (MediaFrameSourceKind.Infrared or MediaFrameSourceKind.Color))
                    continue;

                var isIr = info.SourceKind == MediaFrameSourceKind.Infrared;
                var name = $"{group.DisplayName} [{(isIr ? "IR" : "Color")}, {info.MediaStreamType}]";
                result.Add(new CameraInfo(group.Id, info.Id, name, isIr));
            }
        }

        // IR first, prefer VideoPreview within a group; merge duplicate sources (preview + record).
        return result
            .OrderByDescending(c => c.IsInfrared)
            .ThenBy(c => c.DisplayName.Contains("VideoPreview") ? 0 : 1)
            .GroupBy(c => (c.GroupId, c.IsInfrared))
            .Select(g => g.First())
            .ToList();
    }
}

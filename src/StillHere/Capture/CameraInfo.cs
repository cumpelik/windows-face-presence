namespace StillHere.Capture;

/// <param name="SharesDeviceWithColor">
/// True for a color camera, and for an IR source in a group that also contains a color source (the same physical
/// device). A stand-alone IR camera is preferred, so checks never touch the color camera.
/// </param>
public sealed record CameraInfo(string GroupId, string SourceId, string DisplayName, bool IsInfrared, bool SharesDeviceWithColor)
{
    public string Kind => IsInfrared ? "IR" : "Color";
    public override string ToString() => DisplayName;
}

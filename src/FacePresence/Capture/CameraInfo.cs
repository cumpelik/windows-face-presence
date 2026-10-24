namespace FacePresence.Capture;

public sealed record CameraInfo(string GroupId, string SourceId, string DisplayName, bool IsInfrared)
{
    public string Kind => IsInfrared ? "IR" : "Color";
    public override string ToString() => DisplayName;
}

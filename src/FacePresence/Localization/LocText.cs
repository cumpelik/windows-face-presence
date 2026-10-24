namespace FacePresence.Localization;

/// <summary>A text key with arguments, translated when displayed – so a language switch also re-translates status messages.</summary>
public sealed record LocText(string Key, params object[] Args)
{
    public override string ToString() => L.T(Key, Args);
}

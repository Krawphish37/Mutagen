namespace Mutagen.Bethesda.Starfield;

public partial class EchoMarker
{
    public enum ActionAtLimitType : byte
    {
        Steal = 0,
        Skip = 1,
    }

    public enum UseType : byte
    {
        OnEachPlayback = 0,
        OnLimitReached = 1,
    }
}

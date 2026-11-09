namespace Mutagen.Bethesda.Starfield;

public partial class Effect
{
    // EFIF (xEdit)
    [Flags]
    public enum Flag : uint
    {
        ValueAsRatio = 0x01,
        AlwaysShowInItemCard = 0x02,
    }
}

namespace Mutagen.Bethesda.Starfield;

public partial class ActorValueModulation
{
    public enum GroupType
    {
        None,
        Simple,
        Complex,
        Modulation
    }

    // Starfield's texture types (MODT) start at bit 1; bit 0 has no name (xEdit's Starfield definitions)
    [Flags]
    public enum TextureTypeEnum
    {
        Rough = 0x0002,
        Opacity = 0x0004,
        Normal = 0x0008,
        AO = 0x0010,
        Metal = 0x0020,
        Conditions = 0x0040,
        Emissive = 0x0080,
    }
}

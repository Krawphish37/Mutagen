namespace Mutagen.Bethesda.Starfield;

public partial class CellGrid
{
    [Flags]
    public enum Flag : byte
    {
        HideQuad1 = 0x01,
        HideQuad2 = 0x02,
        HideQuad3 = 0x04,
        HideQuad4 = 0x08,
        NoCollisionQuad1 = 0x10,
        NoCollisionQuad2 = 0x20,
        NoCollisionQuad3 = 0x40,
        NoCollisionQuad4 = 0x80,
    }
}

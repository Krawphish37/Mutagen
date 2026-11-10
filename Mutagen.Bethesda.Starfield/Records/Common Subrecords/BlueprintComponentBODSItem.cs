namespace Mutagen.Bethesda.Starfield;

public partial class BlueprintComponentBODSItem
{
    /// <summary>
    /// Which of the three colors (BODV) the part uses. In Starfield.esm the bits match the non-black colors on
    /// 33,404 of 33,560 items; the other 156 have a bit set with that color black (0,0,0), never the reverse.
    /// </summary>
    [Flags]
    public enum ColorFlag
    {
        Color1 = 0x1,
        Color2 = 0x2,
        Color3 = 0x4,
    }
}

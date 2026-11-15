namespace Mutagen.Bethesda.Starfield;

public partial class Transform
{
    // ToDo
    // Copy Paste Risk
    [Flags]
    public enum MajorFlag
    {
        AroundOrigin = 0x0001_0000,
        // C55: xEdit bit 17 (323 TRNS in Starfield.esm)
        ApplyTranslation = 0x0002_0000,
    }

    // BNAM, xEdit's Basis
    public enum BasisType
    {
        WorldBound = 0,
        ObjectOrigin = 1,
    }

    // ENAM, xEdit's Color Mode Flags
    [Flags]
    public enum ColorModeFlag
    {
        Normal = 0x01,
        Monochromatic = 0x02,
        AlphaFill = 0x04,
    }
}
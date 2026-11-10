namespace Mutagen.Bethesda.Starfield;

public partial class Transform
{
    // ToDo
    // Copy Paste Risk
    [Flags]
    public enum MajorFlag
    {
        AroundOrigin = 0x0000_8000
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
namespace Mutagen.Bethesda.Starfield;

public partial class ObjectPaletteDefaults
{
    [Flags]
    public enum Flag
    {
        ConformToSlope = 0x00001,
        RotateZToSlope = 0x00004,
        SlopLimitIsMinSlope = 0x00008,
        PlaceAsMarker = 0x00010,
        OutsideWaterHeight = 0x00020,
    }

    // Starfield stores X-Large 0, Large 1, Medium 2, Small 3 and None 5; there is no 4
    public enum FootprintSizes
    {
        XLarge = 0,
        Large = 1,
        Medium = 2,
        Small = 3,
        None = 5
    }
}
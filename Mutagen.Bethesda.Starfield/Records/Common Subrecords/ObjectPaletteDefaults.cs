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
        // xEdit reads OPDS flags as one byte and these bits as unused bytes; only PIRocksVolcanicRoughXLarge02 (PKIN 01ED4E:Starfield.esm) sets them
        Unknown12 = 0x01000,
        Unknown13 = 0x02000,
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
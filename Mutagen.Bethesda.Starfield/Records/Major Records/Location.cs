namespace Mutagen.Bethesda.Starfield;

partial class Location
{
    [Flags]
    public enum MajorFlag
    {
        InteriorCellUseRefLocationForWorldMapPlayerMarker = 0x0800,
        // C55: xEdit bit 14, as Cell/Quest/DialogTopic.PartialForm (63 LCTN in the official add-ons)
        PartialForm = 0x0000_4000,
        OffLimits = 0x0002_0000,
        CannotWait = 0x0008_0000,
        PublicArea = 0x0010_0000,
    }
}
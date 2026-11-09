namespace Mutagen.Bethesda.Starfield;

public partial class MiscItem
{
    // Record header flags (xEdit's bit numbers 2, 4, 9, 11, 13; these used to be written as the plain numbers 11 and 13)
    [Flags]
    public enum MajorFlag
    {
        NonPlayable = 0x0000_0004,
        GroundPiece = 0x0000_0010,
        HiddenFromLocalMap = 0x0000_0200,
        CalcFromComponents = 0x0000_0800,
        PackInUseOnly = 0x0000_2000,
    }

    // FLAG (xEdit; same list as KEYM)
    [Flags]
    public enum Flag
    {
        AllowQuestItemCrafting = 0x01,
        NonInstancedKey = 0x02,
    }
}

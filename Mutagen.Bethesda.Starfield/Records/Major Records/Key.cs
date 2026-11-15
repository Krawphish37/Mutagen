namespace Mutagen.Bethesda.Starfield;

partial class Key
{
    [Flags]
    public enum MajorFlag
    {
        // C55: was 0x02. xEdit bit 2 (its 0x00000002 comment is a slip), Skyrim KEYM and every other Starfield
        // Non-Playable header flag are 0x04; no plugin sets 0x02 on any record
        NonPlayable = 0x0000_0004,
        CalcValueFromComponents = 0x0000_0800,
        PackInUseOnly = 0x0000_2000
    }
    
    [Flags]
    public enum Flag
    {
        AllowQuestItemCrafting = 0x01,
        NonInstancedKey = 0x02,
    }
}
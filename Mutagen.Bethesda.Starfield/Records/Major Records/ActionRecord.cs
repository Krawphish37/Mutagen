namespace Mutagen.Bethesda.Starfield;

public partial class ActionRecord
{
    [Flags]
    public enum MajorFlag
    {
        // C55: was 0x0008_0000 (xEdit Fallout 4's 0x00080000 comment). xEdit bit 15, as Keyword.Restricted (C54)
        Restricted = 0x0000_8000
    }
}
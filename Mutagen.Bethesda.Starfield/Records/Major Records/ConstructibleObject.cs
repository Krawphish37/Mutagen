using Noggog;

namespace Mutagen.Bethesda.Starfield;

public partial class ConstructibleObject
{
    [Flags]
    public enum MajorFlag
    {
        // C55: was 0x0008_0000, set on no COBJ. xEdit bit 26; 401 COBJ in Starfield.esm set it, 376 of them create a FLST
        ListContainsVariants = 0x0400_0000
    }
    
    public enum LearnMethodEnum
    {
        PickupOrScript,
        Scrapping,
        Ingesting,
        DefaultOrConditions,
        Plan,
    }

    [Flags]
    public enum Flag
    {
        FilterNotRequiredToLearn = 0x00000001,
        Unknown1 = 0x0000_0002,
        ExcludeFromWorkshopLod = 0x00000004,
    }
}
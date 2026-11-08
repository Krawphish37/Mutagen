using Mutagen.Bethesda.Starfield.Internals;

namespace Mutagen.Bethesda.Starfield;

public partial class Armor
{
    [Flags]
    public enum MajorFlag
    {
        NonPlayable = 0x0000_0004,
        Shield = 0x0000_0040
    }

    public const string ObjectModificationName = "TESObjectARMOR_InstanceData";

    public enum Property
    {
        ActorValue = RecordTypeInts.AACT,
        DamageResistance = RecordTypeInts.ADMG,
        Enchantment = RecordTypeInts.AENC,
        Keyword = RecordTypeInts.AKEY,
        LayeredMaterialSwap = RecordTypeInts.AMLS,
        Rating = RecordTypeInts.ARAT,
        Value = RecordTypeInts.AVAL,
        Weight = RecordTypeInts.AWGT,
        BashImpactDataSet = RecordTypeInts.ABBI,
        BlockMaterial = RecordTypeInts.ABBM,
        BodyPart = RecordTypeInts.ABOD,
        ColorRemappingIndex = RecordTypeInts.ACOL,
        Health = RecordTypeInts.AHLT,
        AddonIndex = RecordTypeInts.AIND,
        ModCount = RecordTypeInts.ATMC,
        // Object templates (OBTS) on the record itself store these small ids, not the 4-letter OMOD codes above (xEdit wbObjectModPropertiesARMOEnum).
        TemplateLayeredMaterialSwap = 14,
    }
}
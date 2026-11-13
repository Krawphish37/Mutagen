using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Masters;

internal static class MasterStyleConstruction
{
    public static MasterStyle ConstructFromFlags(int flags, GameConstants constants)
    {
        bool small = constants.SmallMasterFlag.HasValue && Enums.HasFlag(flags, constants.SmallMasterFlag.Value);
        bool medium = constants.MediumMasterFlag.HasValue && Enums.HasFlag(flags, constants.MediumMasterFlag.Value);
        
        // Both the Small and the Medium flag: read as Small, as xEdit does (it gives such a plugin a light slot, the Light
        // flag checked first, and refuses to save it with both). Writing one with both flags set is refused
        // (ModHeaderWriteLogic); HeaderSizeFlags Auto writes it as the Small master it was read as.
        if (small) return MasterStyle.Small;
        if (medium) return MasterStyle.Medium;
        return MasterStyle.Full;
    }
}
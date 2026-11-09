using System.Diagnostics;
namespace Mutagen.Bethesda.Starfield;

public partial class ObjectEffect
{
    // ENIT flags and enchant type (xEdit)
    [Flags]
    public enum Flag
    {
        NoAutoCalc = 0x01,
        ExtendDurationOnRecast = 0x04,
    }

    public enum EnchantTypeEnum
    {
        Enchantment = 0x06,
        StaffEnchantment = 0x0C,
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    IReadOnlyList<IEffectGetter> IHasEffectsGetter.Effects => Effects;
}
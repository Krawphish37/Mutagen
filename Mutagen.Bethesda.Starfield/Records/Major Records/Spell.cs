using System.Diagnostics;
namespace Mutagen.Bethesda.Starfield;

public partial class Spell
{
    [Flags]
    public enum Flag
    {
        ManualCostCalc = 0x0000_0001,
        InstantCast = 0x0004_0000,
        AreaEffectIgnoresLOS = 0x0008_0000,
        IgnoreResistance = 0x0010_0000,
        NoAbsorbOrReflect = 0x0020_0000,
    }
    
    /// <summary>
    /// One type per spell (not flags), named as xEdit names them
    /// </summary>
    public enum SpellType
    {
        Spell = 0,
        Disease = 1,
        Power = 2,
        LesserPower = 3,
        Ability = 4,
        Poison = 5,
        Enchantment = 6,
        Potion = 7,
        WortCraft = 8,
        LeveledSpell = 9,
        Addiction = 10,
        VoicePower = 11,
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    IReadOnlyList<IEffectGetter> IHasEffectsGetter.Effects => Effects;
}
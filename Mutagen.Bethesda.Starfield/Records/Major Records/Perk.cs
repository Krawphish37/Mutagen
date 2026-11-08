namespace Mutagen.Bethesda.Starfield;

public partial class Perk
{
    [Flags]
    public enum MajorFlag
    {
        NonPlayable = 0x4
    }
    
    public enum PerkCrewAssignment
    {
        None,
        CrewShip,
        CrewOutpost,
    }

    [Flags]
    public enum Flag
    {
        PcTrait = 0x001,
        PcPlayable = 0x002,
        Hidden = 0x004,
        ShowInCrewUi = 0x008,
        PcBackground = 0x010,
        HiddenOnDataMenu = 0x020,
    }

    public enum EffectType
    {
        Quest,
        Ability,
        EntryPoint,
    }
}
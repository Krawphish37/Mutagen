namespace Mutagen.Bethesda.Starfield;

partial class CombatStyle
{
    [Flags]
    public enum MajorFlag
    {
        AllowDualWielding = 0x0008_0000
    }

    [Flags]
    public enum Flag
    {
        Dueling = 0x01,
        Flanking = 0x02,
        AllowDualWielding = 0x04,
        Charging = 0x08,
        RetargetAnyNearbyMeleeTarget = 0x10,
        PreferWalkingWhileStrafing = 0x20,
        UseGroupCohesion = 0x40,
        MaintainAttackFromCoverUntilSuppressed = 0x80,
    }

    // CSTY FNAM. Unnamed in xEdit (wbUnknown). Bit i is set exactly where UNAM template slot i holds the Template
    // Default Combat Style (TNAM): 160 of 161 combat styles in Starfield.esm; the other has every bit set and no
    // templates at all. The names follow the UNAM slots.
    [Flags]
    public enum TemplateFlag
    {
        OffensiveDefensiveMults = 0x0_0001,
        AvoidThreats = 0x0_0002,
        EquipmentScoreMults = 0x0_0004,
        Melee = 0x0_0008,
        Ranged = 0x0_0010,
        CloseRange = 0x0_0020,
        LongRange = 0x0_0040,
        Cover = 0x0_0080,
        Flight = 0x0_0100,
        Tunnel = 0x0_0200,
        SpaceGeneral = 0x0_0400,
        SpacePower = 0x0_0800,
        SpaceRepair = 0x0_1000,
        SpaceApproaching = 0x0_2000,
        SpaceDetection = 0x0_4000,
        Misc = 0x0_8000,
        SpaceManeuverTypes = 0x1_0000,
    }
}
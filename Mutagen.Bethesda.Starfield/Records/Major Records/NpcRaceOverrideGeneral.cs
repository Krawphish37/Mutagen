namespace Mutagen.Bethesda.Starfield;

partial class NpcRaceOverrideGeneral
{
    /// <summary>
    /// Which of the NPC's race overrides are active (xEdit's ONA2 General\Active Overrides).
    /// </summary>
    [Flags]
    public enum ActiveOverride : uint
    {
        Size = 0x0000_0001,
        MovementAcceleration = 0x0000_0002,
        MovementDeceleration = 0x0000_0004,
        MovementAngularAcceleration = 0x0000_0008,
        MovementAngularTolerance = 0x0000_0010,
        FlightRadius = 0x0000_0020,
        UnarmedWeapon = 0x0000_0040,
        InjuredHealthPercent = 0x0000_0080,
        BleedoutHealthPercent = 0x0000_0100,
        BleedoutChance = 0x0000_0200,
        BleedoutRecoverChance = 0x0000_0400,
        BleedoutTimeMin = 0x0000_0800,
        BleedoutTimeMax = 0x0000_1000,
        BleedoutHealthDrainRate = 0x0000_2000,
        ImpactMaterialType = 0x0000_4000,
        CorpseOpenSound = 0x0004_0000,
        CorpseCloseSound = 0x0008_0000,
        OnCripple = 0x0040_0000,
        ElectromagneticShockSupported = 0x0080_0000,
        ElectromagneticShockRecoverChance = 0x0100_0000,
        ElectromagneticShockTimeMin = 0x0200_0000,
        ElectromagneticShockTimeMax = 0x0400_0000,
        ElectromagneticShockRecoverChanceOnDamage = 0x0800_0000,
        ElectromagneticShockHealthDrainRate = 0x1000_0000,
        ElectromagneticShockHealthAfterRecovery = 0x2000_0000,
        ElectromagneticShockImmunityDuration = 0x4000_0000,
    }
}

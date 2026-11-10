namespace Mutagen.Bethesda.Starfield;

partial class Terminal
{
    [Flags]
    public enum MajorFlag
    {
        HasDistantLod = 0x0000_8000,
        RandomAnimStart = 0x0001_0000
    }

    public enum BackgroundType
    {
        Constellation,
        FreestarCollective,
        Default,
        NASA,
        RyujinIndustries,
        SlaytonAerospace,
        UnitedColonies,
        CrimsonFleet,
        // xEdit's art theme list (8 is unused there too). No plugin we have uses 9-11 yet.
        HouseVaruun = 9,
        ShipUpgrade = 10,
        TerranArmada = 11,
    }

    [Flags]
    public enum Flag
    {
        NonPlanar = 0x01,
        IgnoredBySandbox = 0x02,
        Unknown2 = 0x04,
        IsARadio = 0x10,
        AllowWaterDisplacements = 0x20,
    }
}
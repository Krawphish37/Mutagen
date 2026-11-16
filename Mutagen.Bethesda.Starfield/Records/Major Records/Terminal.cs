namespace Mutagen.Bethesda.Starfield;

partial class Terminal
{
    [Flags]
    public enum MajorFlag
    {
        // C55: NonOccluder, PackInUseOnly and the three Navmesh members are xEdit's names for bits set in Starfield.esm
        // (28, 28, 4, 129 and 65 TERM)
        NonOccluder = 0x0000_0010,
        PackInUseOnly = 0x0000_2000,
        HasDistantLod = 0x0000_8000,
        RandomAnimStart = 0x0001_0000,
        NavmeshBoundingBox = 0x0800_0000,
        NavmeshIgnoreErosion = 0x2000_0000,
        NavmeshGround = 0x4000_0000,
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
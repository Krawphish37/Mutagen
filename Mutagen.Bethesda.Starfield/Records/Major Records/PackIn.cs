namespace Mutagen.Bethesda.Starfield;

partial class PackIn
{
    // Record header flags (xEdit calls 0x200 Instanced)
    [Flags]
    public enum MajorFlag
    {
        Prefab = 0x0000_0200,
        CalcLPIs = 0x0000_0400,
        InstancedStatic = 0x0000_0800,
        PackInUseOnly = 0x0000_2000,
        Obstacle = 0x0200_0000,
    }

    // FNAM (xEdit). Bit 0x01 is set on many records but has no name.
    [Flags]
    public enum Flag
    {
        Unknown0 = 0x01,
        GroupedWorkshopObject = 0x02,
        Fixed = 0x04,
        Grouped = 0x08,
        OneMotion = 0x10,
        UserDefinedPrimitives = 0x20,
        OverrideDirtiness = 0x40,
        // Bits 7-30 are set only on OPMinePod18MHexA01PackIn (PKIN 0071E9:Starfield.esm)
        Unknown7 = 0x80,
        Unknown8 = 0x100,
        Unknown10 = 0x400,
        Unknown12 = 0x1000,
        Unknown13 = 0x2000,
        Unknown16 = 0x1_0000,
        Unknown17 = 0x2_0000,
        Unknown18 = 0x4_0000,
        Unknown19 = 0x8_0000,
        Unknown21 = 0x20_0000,
        Unknown27 = 0x800_0000,
        Unknown28 = 0x1000_0000,
        Unknown30 = 0x4000_0000,
    }
}

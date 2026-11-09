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
        GroupedWorkshopObject = 0x02,
        Fixed = 0x04,
        Grouped = 0x08,
        OneMotion = 0x10,
        UserDefinedPrimitives = 0x20,
        OverrideDirtiness = 0x40,
    }
}

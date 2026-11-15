namespace Mutagen.Bethesda.Starfield;

public partial class StaticCollection
{
    [Flags]
    public enum MajorFlag
    {
        // C55: was 0x04 (the bit number). xEdit bit 4, as Static.NonOccluder
        NonOccluder = 0x00000010,
        HiddenFromLocalMap = 0x00000200,
        Loadscreen = 0x00000400,
        UsedAsPlatform = 0x00000800,
        HasDistantLod = 0x00008000,
        Obstacle = 0x02000000,
        NavMeshGenerationFilter = 0x04000000,
        NavMeshGenerationBoundingBox = 0x08000000,
        NavMeshGenerationGround = 0x40000000
    }
}
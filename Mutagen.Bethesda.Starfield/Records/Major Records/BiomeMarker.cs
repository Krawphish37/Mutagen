namespace Mutagen.Bethesda.Starfield;

partial class BiomeMarker
{
    // Record header flags, xEdit's BMMO list. Only Calculate Once Per Location is set in the data (48 BMMO in
    // Starfield.esm, the prey/predator markers); the rest is xEdit's common list. Bits 28 and 29 carry two names in
    // xEdit ('Navmesh - Only Cut/Show in world map', 'Navmesh - Ignore Erosion/Child Can Use'); named here as the other
    // Starfield enums name them. uint because it names bit 31 (C71: see NavigationMesh.MajorFlag).
    [Flags]
    public enum MajorFlag : uint
    {
        NonPlayable = 0x0000_0004,
        GroundPiece = 0x0000_0010,
        HiddenFromLocalMap = 0x0000_0200,
        UsedAsPlatform = 0x0000_0800,
        HasCurrents = 0x0008_0000,
        CalculateOncePerLocation = 0x0010_0000,
        Obstacle = 0x0200_0000,
        NavmeshFilter = 0x0400_0000,
        NavmeshBoundingBox = 0x0800_0000,
        NavmeshOnlyCut = 0x1000_0000,
        NavmeshIgnoreErosion = 0x2000_0000,
        NavmeshGround = 0x4000_0000,
        MustBeUnique = 0x8000_0000,
    }
}

namespace Mutagen.Bethesda.Starfield;

public partial class NavigationMesh
{
    // C71: uint, because it names bit 31. A flag enum that names bit 31 is uint: an int member at 0x8000_0000 is
    // negative, and Noggog's EnumerateContainedFlags (used by the JSON/YAML serializer) never returns for such an enum.
    [Flags]
    public enum MajorFlag : uint
    {
        Autogen = 0x0400_0000,
        // C55: xEdit bit 31 'Navmesh Gen Cell' (13 NAVM in Starfield.esm)
        NavmeshGenCell = 0x8000_0000,
    }
}
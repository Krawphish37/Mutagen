namespace Mutagen.Bethesda.Starfield;

public partial class NavigationMesh
{
    [Flags]
    public enum MajorFlag
    {
        Autogen = 0x0400_0000,
        // C55: xEdit bit 31 'Navmesh Gen Cell' (13 NAVM in Starfield.esm). The enum stays int-based, as the others.
        NavmeshGenCell = unchecked((int)0x8000_0000),
    }
}
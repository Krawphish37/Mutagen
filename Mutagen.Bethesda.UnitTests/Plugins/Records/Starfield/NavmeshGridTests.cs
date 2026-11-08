using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A worldspace navmesh's parent grid (NVNM) is stored Y then X (xEdit "Grid Y", "Grid X"; checked against the parent
// cell's XCLC on 9 navmeshes). Mutagen read it X then Y, so X and Y were swapped.
public class NavmeshGridTests
{
    // NVNM of NAVM 14068B (159 bytes): worldspace 01251B, Grid Y -2, Grid X 2
    private const string Nvnm =
        "11-00-00-00-3C-A0-E9-A5-1B-25-01-00-FE-FF-02-00-03-00-00-00-33-F3-57-43-9A-99-23-C3-1C-B4-E7-41-01-00-00-00-00-00-55-43-00-C0-25-C3-E8-80-CE-41-01-00-00-00-9A-19-56-43-00-40-27-C3-84-1A-CA-41-01-00-00-00-01-00-00-00-00-00-01-00-02-00-FF-FF-FF-FF-FF-FF-FF-FF-7F-7F-04-00-18-00-C0-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-C0-CC-3C-40-80-99-69-40-00-00-55-43-00-40-27-C3-84-1A-CA-41-33-F3-57-43-9A-99-23-C3-FF-FF-7F-7F-01-00-00-00-00-00-0C-00-08-00";

    private static IEnumerable<INavigationMeshGetter> ReadNavmesh() =>
        ReadBoth<INavigationMeshGetter>(Record("NAVM", Subrecord("NVNM", Nvnm)), f => NavigationMesh.CreateFromBinary(f), (s, p) => NavigationMeshBinaryOverlay.NavigationMeshFactory(s, p));

    [Fact]
    public void Grid_Reads_X_Then_Y()
    {
        foreach (var navmesh in ReadNavmesh())
        {
            var parent = navmesh.NavmeshGeometry!.Parent.ShouldBeAssignableTo<IWorldspaceNavmeshParentGetter>()!;
            parent.Parent.FormKey.ID.ShouldBe(0x01251Bu);
            parent.Coordinates.ShouldBe(new P2Int16(2, -2));
        }
    }

    [Fact]
    public void Grid_WritesSameBytes()
    {
        foreach (var navmesh in ReadNavmesh())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => navmesh.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("NVNM")).Content.ToArray().ShouldBe(Hex(Nvnm));
        }
    }

    [Fact]
    public void Grid_Writes_Y_First()
    {
        var parent = new WorldspaceNavmeshParent { Coordinates = new P2Int16(5, -7) };
        var bytes = Write(w => parent.WriteToBinary(w));
        bytes.AsSpan(4, 4).ToArray().ShouldBe(new byte[] { 0xF9, 0xFF, 0x05, 0x00 });
    }
}

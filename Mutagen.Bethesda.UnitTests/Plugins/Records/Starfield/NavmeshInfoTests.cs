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

// NAVI navmesh info (NVMI) decoded per xEdit. Mutagen kept Flags, Approx Location and Preferred % as 20 raw bytes and
// everything after the door links as raw bytes, which hid the traversal links (Cell or Object, Traversal, From/To
// Navmesh) and the pathing cell's parent world or cell from FormID remapping. All 56,631 NVMI in Starfield.esm fit the
// layout exactly.
public class NavmeshInfoTests
{
    // NVMI of NAVM 31627C: 1 edge link, 1 door link, 1 traversal; parent world 2BA5C5, grid Y -1, X 1; unknown byte 0x0C
    private const string WithTraversal =
        "7C-62-31-00-00-00-00-00-56-5A-F6-42-43-BB-21-C2-4F-69-1A-42-00-00-00-00-01-00-00-00-7A-61-31-00-00-00-00-00-01-00-00-00-F3-73-8B-E4-D9-52-00-00-01-00-00-00-6D-4F-F9-DA-C0-A5-2B-00-F2-AF-00-00-21-3B-0D-43-21-30-27-C2-FB-2B-EF-41-DB-22-0B-43-B8-60-2B-C2-32-67-1A-41-CF-CF-6E-3F-E1-05-88-40-D8-68-AD-3F-04-00-00-00-7C-62-31-00-7B-62-31-00-00-3C-A0-E9-A5-C5-A5-2B-00-FF-FF-01-00-0C-00-00-00-00";
    // NVMI of NAVM 1E410D: Is Island, island data (2 triangles, 4 vertices), parent cell 273765
    private const string WithIsland =
        "0D-41-1E-00-20-00-00-00-E2-6E-5F-43-3A-2C-B5-C2-DC-27-8E-42-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-66-A9-5E-43-4C-75-B7-C2-DC-27-8E-42-CC-4F-61-43-7F-28-B2-C2-DC-27-8E-42-02-00-00-00-00-00-01-00-02-00-02-00-01-00-03-00-04-00-00-00-66-A9-5E-43-7F-28-B2-C2-DC-27-8E-42-66-A9-5E-43-4C-75-B7-C2-DC-27-8E-42-CC-4F-61-43-7F-28-B2-C2-DC-27-8E-42-CC-4F-61-43-4C-75-B7-C2-DC-27-8E-42-3C-A0-E9-A5-00-00-00-00-65-37-27-00-00-00-00-00-00";

    private static IEnumerable<INavigationMeshInfoMapGetter> Read(string nvmi) =>
        ReadBoth<INavigationMeshInfoMapGetter>(Record("NAVI", Subrecord("NVMI", nvmi)), f => NavigationMeshInfoMap.CreateFromBinary(f), (s, p) => NavigationMeshInfoMapBinaryOverlay.NavigationMeshInfoMapFactory(s, p));

    [Fact]
    public void Traversal_And_World_Read()
    {
        foreach (var navi in Read(WithTraversal))
        {
            var info = navi.MapInfos.Single();
            info.NavigationMesh.FormKey.ID.ShouldBe(0x31627Cu);
            info.Flags.ShouldBe((NavigationMapInfo.Flag)0);
            info.MergedTo.Single().FormKey.ID.ShouldBe(0x31617Au);
            info.LinkedDoors.Single().Door.FormKey.ID.ShouldBe(0x52D9u);
            var trav = info.Traversals.Single();
            trav.CellOrObject.FormKey.ID.ShouldBe(0x2BA5C0u);
            trav.Traversal.FormKey.ID.ShouldBe(0xAFF2u);
            trav.Flags.ShouldBe(4u);
            trav.FromNavmesh.FormKey.ID.ShouldBe(0x31627Cu);
            trav.ToNavmesh.FormKey.ID.ShouldBe(0x31627Bu);
            info.IslandData.ShouldBeNull();
            info.PathingCell.ParentWorld.FormKey.ID.ShouldBe(0x2BA5C5u);
            info.PathingCell.Grid.ShouldBe(new P2Int16(1, -1));
            info.UnknownByte.ShouldBe((byte)0x0C);
            info.UnknownCrcs.ShouldBeEmpty();
            info.Unknown.Length.ShouldBe(0);
            var links = navi.EnumerateFormLinks().Select(l => l.FormKey.ID).ToArray();
            foreach (var id in new uint[] { 0x2BA5C0, 0xAFF2, 0x31627B, 0x2BA5C5 }) links.ShouldContain(id);
        }
    }

    [Fact]
    public void Island_And_Cell_Read()
    {
        foreach (var navi in Read(WithIsland))
        {
            var info = navi.MapInfos.Single();
            info.Flags.ShouldBe(NavigationMapInfo.Flag.IsIsland);
            info.Traversals.ShouldBeEmpty();
            var island = info.IslandData.ShouldNotBeNull();
            island.Triangles.Count.ShouldBe(2);
            island.Vertices.Count.ShouldBe(4);
            info.PathingCell.ParentWorld.IsNull.ShouldBeTrue();
            info.PathingCell.ParentCell.FormKey.ID.ShouldBe(0x273765u);
            navi.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x273765u);
        }
    }

    [Theory]
    [InlineData(WithTraversal)]
    [InlineData(WithIsland)]
    public void Nvmi_WritesSameBytes(string nvmi)
    {
        foreach (var navi in Read(nvmi))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => navi.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("NVMI")).Content.ToArray().ShouldBe(Hex(nvmi));
        }
    }
}

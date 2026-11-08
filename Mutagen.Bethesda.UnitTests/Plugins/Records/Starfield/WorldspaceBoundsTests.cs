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

// Worldspace bounds (NAM0/NAM9) are stored in units and shown in cells; a Starfield cell is 100 units (xEdit:
// IsSF1(1/100, 1/4096)), not Skyrim's 4096. Worldspace 3CB2D0 in Starfield.esm stores -200/-200 and 300/300,
// i.e. cells -2 to 3, which the 4096 scale read as -0.0488 and 0.0732.
public class WorldspaceBoundsTests
{
    [Fact]
    public void Bounds_ReadInCells()
    {
        var bytes = Record("WRLD", Subrecord("NAM0", "00-00-48-C3-00-00-48-C3"), Subrecord("NAM9", "00-00-96-43-00-00-96-43"));
        foreach (var world in ReadBoth<IWorldspaceGetter>(bytes, f => Worldspace.CreateFromBinary(f), (s, p) => WorldspaceBinaryOverlay.WorldspaceFactory(s, p)))
        {
            world.ObjectBoundsMin.ShouldBe(new P2Float(-2, -2));
            world.ObjectBoundsMax.ShouldBe(new P2Float(3, 3));
        }
    }

    [Fact]
    public void Bounds_WriteInUnits()
    {
        var world = new Worldspace(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            ObjectBoundsMin = new P2Float(-2, -2),
            ObjectBoundsMax = new P2Float(3, 3),
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => world.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("NAM0")).Content.ToArray().ShouldBe(Hex("00-00-48-C3-00-00-48-C3"));
        written.FindSubrecord(new RecordType("NAM9")).Content.ToArray().ShouldBe(Hex("00-00-96-43-00-00-96-43"));
    }
}

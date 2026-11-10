using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Water current velocities (CELL and REFR): XWCN is the velocity count (u32) and XWCU the velocities, 16 bytes each
// (xEdit: Velocity vec3 + an unnamed 4 bytes). Mutagen had, on REFR, XWCN as a FormLink to a Static (the count 3 was a
// fake link that FormID remapping would rewrite) and XWCU as raw bytes; on CELL, XWCN as raw bytes and XWCU as
// Fallout 4's layout (Offset, Unknown, Angle, UnknownBytes). Now both have WaterCurrentVelocities (XWCU), a list
// counted by XWCN: the count is written from the list and is no longer a field of its own.
// The water current data on REFR (XCZR/XCZC + XCZA) names XCZR Reference.
// Starfield.esm: XWCN 3 or 4 on all 4 records that have it (2 CELL, 2 REFR), XWCU 48 or 64 bytes.
public class WaterCurrentTests
{
    // XWCN + XWCU of BanditTrail06 (2AD41B:Starfield.esm): 4 velocities
    private const string CellCount = "04-00-00-00";
    private const string CellVelocities = "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-99-BA-FB-42-05-38-E3-C2-BD-16-75-42-03-00-00-00-00-00-00-00-00-00-80-BF-00-00-00-80-00-00-80-3F";

    // Whole REFR 007064 (Starfield.esm): XLYR, BOLV, XWCN 3, XWCU, XSCL, DATA (its DATA holds a -0.0 that Mutagen writes
    // as 0, so only XWCN and XWCU are compared)
    private const string Ref007064 = "4E414D4504004F080300584C5952040027B12500424F4C56020001005857434E0400030000005857435530000000000000000000000000000000000000000000000000000000000000000000879EC5C183A13FC20000803F010000005853434C04000000C03F444154411800E373FAC1C0D26FC25FE692422852ABA700000080FFFF4137";

    // REFR 3194D8 (Starfield.esm) without its XPRM (a separate issue: the primitive colour floats are stored as bytes):
    // water current data, two references (25CA9B, 007064) and a cell
    // (NewAtlantisSpaceport 0125B6:Starfield.esm), each with XCZA
    private const string Ref3194D8 = "4E414D450400C400000058435A5204009BCA250058435A4104000000000058435A5204006470000058435A4104000000000058435A430400B625010058435A41040000000000444154411800879EC5C183A13FC29089AE42000000000000000000000000";

    [Fact]
    public void Cell_Velocities()
    {
        var bytes = Record("CELL", Subrecord("XWCN", CellCount), Subrecord("XWCU", CellVelocities));
        foreach (var cell in ReadBoth<ICellGetter>(bytes, f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p)))
        {
            var v = cell.WaterCurrentVelocities!;
            v.Count.ShouldBe(4);
            v[2].Velocity.X.ShouldBe(125.86445f, 0.0001f);
            v[2].Velocity.Y.ShouldBe(-113.60941f, 0.0001f);
            v[3].Velocity.Y.ShouldBe(-1f);
            v[2].Unknown.ToArray().ShouldBe(Hex("03-00-00-00"));
            var written = GameConstants.Starfield.MajorRecord(Write(w => cell.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XWCN")).Content.ToArray().ShouldBe(Hex(CellCount));
            // -0.0 (the 4th velocity's Z) is written back as -0.0 on the fixes chain (and with C80); everything is written back as read.
            written.FindSubrecord(new RecordType("XWCU")).Content.ToArray().ShouldBe(Hex(CellVelocities));
        }
    }

    [Fact]
    public void PlacedObject_Velocities()
    {
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(Ref007064)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var v = refr.WaterCurrentVelocities!;
            v.Count.ShouldBe(3);
            v[0].Velocity.X.ShouldBe(0f);
            v[2].Velocity.X.ShouldBe(-24.702406f, 0.0001f);
            v[2].Velocity.Y.ShouldBe(-47.907726f, 0.0001f);
            v[2].Velocity.Z.ShouldBe(1f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XWCN")).Content.ToArray().ShouldBe(Hex("03-00-00-00"));
            written.FindSubrecord(new RecordType("XWCU")).Content.ToArray().ShouldBe(Hex(Ref007064).AsSpan(0x2C, 48).ToArray());
        }
    }

    [Fact]
    public void PlacedObject_WaterCurrentData()
    {
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(Ref3194D8)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.WaterCurrentReferences.Select(x => x.Reference.FormKey.ID).ShouldBe(new uint[] { 0x25CA9B, 0x007064 });
            refr.CurrentZoneCell!.CurrentZoneCell.FormKey.ID.ShouldBe(0x0125B6u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Ref3194D8));
        }
    }

    [Fact]
    public void Count_IsWrittenFromTheList()
    {
        // XWCN is written from the list size.
        var refr = new PlacedObject(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        refr.WaterCurrentVelocities = new() { new WaterCurrentVelocity(), new WaterCurrentVelocity() };
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((IPlacedObjectGetter)refr).WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XWCN")).Content.ToArray().ShouldBe(Hex("02-00-00-00"));
        written.FindSubrecord(new RecordType("XWCU")).ContentLength.ShouldBe(32);
    }
}

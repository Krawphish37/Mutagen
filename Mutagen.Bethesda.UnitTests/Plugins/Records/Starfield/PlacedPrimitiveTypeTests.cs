using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield placed primitive (XPRM) can be a Cylinder, stored as 7 (xEdit's Starfield definitions;
// 328 of 23,749 primitives in Starfield.esm).
public class PlacedPrimitiveTypeTests
{
    [Fact]
    public void Cylinder_Reads()
    {
        // REFR 1CBC24 in Starfield.esm: XPRM type 7
        var bytes = Record("REFR", Subrecord("XPRM",
            "44-00-6C-42-44-00-6C-42-98-B2-51-41-00-00-00-00-00-00-80-3F-00-00-00-00-9A-99-99-3E-07-00-00-00"));
        foreach (var placed in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            placed.Primitive!.Type.ShouldBe(PlacedPrimitive.TypeEnum.Cylinder);
        }
    }

    [Fact]
    public void Cylinder_Writes()
    {
        var placed = new PlacedObject(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Primitive = new PlacedPrimitive { Type = PlacedPrimitive.TypeEnum.Cylinder },
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("XPRM")).Content.Slice(28, 4)).ShouldBe(7u);
    }
}

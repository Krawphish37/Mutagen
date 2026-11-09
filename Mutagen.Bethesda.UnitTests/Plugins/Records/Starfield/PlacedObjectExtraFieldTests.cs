using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// On a placed object, BOLV is xEdit's Water Reflection (u16; xEdit's enum only has Unknown 0 and Unknown 1; 1 on all 23
// in Starfield.esm) and XFLG is Extra Flags (u32 flags; only bit 6, Unknown 6, is listed; 0x40 on all 30). Both were
// fields named after their signatures (BOLV a UInt16, XFLG raw bytes).
public class PlacedObjectExtraFieldTests
{
    // Whole REFR 2E2C22 (Starfield.esm) of Power_DirectSource_01 (0E6A0A:Starfield.esm): NAME, XFLG, XLYR, DATA
    private const string ExtraFlags = "4E414D4504000A6A0E0058464C47040040000000584C5952040067BA2600444154411800466EC2C2049932C269F56542000000000000000000000000";

    // Whole REFR 007064 (Starfield.esm): XLYR, BOLV 1, XWCN, XWCU, XSCL, DATA
    private const string WaterReflection = "4E414D4504004F080300584C5952040027B12500424F4C56020001005857434E0400030000005857435530000000000000000000000000000000000000000000000000000000000000000000879EC5C183A13FC20000803F010000005853434C04000000C03F444154411800E373FAC1C0D26FC25FE692422852ABA700000080FFFF4137";

    private static IEnumerable<IPlacedObjectGetter> Read(string hex)
        => ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(hex)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    [Fact]
    public void ExtraFlags_Unknown6()
    {
        foreach (var refr in Read(ExtraFlags))
        {
            refr.ExtraFlags.ShouldBe(PlacedObject.ExtraFlag.Unknown6);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(ExtraFlags));
        }
    }

    [Fact]
    public void WaterReflection_Is1()
    {
        foreach (var refr in Read(WaterReflection))
        {
            refr.WaterReflection.ShouldBe((ushort)1);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            // Only BOLV: the DATA rotation holds a -0.0 that Mutagen writes as 0 (not part of this change).
            written.FindSubrecord(new RecordType("BOLV")).Content.ToArray().ShouldBe(Hex("01-00"));
        }
    }
}

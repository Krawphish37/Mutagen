using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A placed primitive's colour (XPRM) is four floats, R G B A (xEdit wbFloatRGBA). Mutagen read R G B into a byte Color
// (NoAlphaFloat), with A in the separate float Unknown, so every channel that isn't exactly n/255 changed on write:
// 0.8000001 became 0.8 (204/255), 0.5 became 0.50196. Starfield.esm: 10,795 of 23,749 XPRM changed (0.9 on 7,150
// channels, 0.20000002 on 1,617, 0.8000001 on 1,580 ...). Now Color is a P3Float (R, G, B as X, Y, Z; the same as the
// fixes chain, cae2b97266) and the 4th float stays Unknown (alpha); all written back as read.
public class PlacedPrimitiveColorTests
{
    // Whole REFR 22920E (Starfield.esm) of ShipModuleTrigger (2C2A47:Starfield.esm): XPRM colour 0.8000001, 0.29803923,
    // 0.20000002, alpha 0.3; then a grouped pack-in, XBPO and DATA
    private const string ShipModuleTrigger = "4E414D450400472A2C005850524D2000676626403333F340302E3C40CECC4C3F9998983ECECC4C3E9A99993E010000005857504B0000474E414D040045530100484E414D0400AF672000494E414D020000004A4E414D0400455301004C4E414D0400000000005857504B00005842504F04000A000000444154411800084197BEA8F83D41D3C51BC0000000000000000000000000";

    // Whole REFR 12E41F (Starfield.esm) of INT_Ship_Module_Hab_Stroud_CompanionWay (03E4CB:Starfield.esm): colour 1, 0.2, 0.9, alpha 0.3
    private const string CompanionWay = "4E414D450400CBE403005850524D20003333834033338340CDCCEC3F0000803FCDCC4C3E6666663F9A99993E01000000584C59520400E358070044415441180000004035000080B400000000000000000000000000000000";

    private static IEnumerable<IPlacedObjectGetter> Read(string hex)
        => ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(hex)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    private static float F(string hex) => BitConverter.ToSingle(Hex(hex));

    [Fact]
    public void Color_IsFourFloats_WrittenBackAsRead()
    {
        foreach (var refr in Read(ShipModuleTrigger))
        {
            var p = refr.Primitive!;
            p.Color.X.ShouldBe(F("CE-CC-4C-3F"));
            p.Color.Y.ShouldBe(F("99-98-98-3E"));
            p.Color.Z.ShouldBe(F("CE-CC-4C-3E"));
            p.Unknown.ShouldBe(F("9A-99-99-3E"));
            p.Type.ShouldBe(PlacedPrimitive.TypeEnum.Box);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(ShipModuleTrigger));
        }
    }

    [Fact]
    public void Color_Point9()
    {
        foreach (var refr in Read(CompanionWay))
        {
            var p = refr.Primitive!;
            p.Color.X.ShouldBe(1f);
            p.Color.Y.ShouldBe(0.2f);
            p.Color.Z.ShouldBe(0.9f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(CompanionWay));
        }
    }
}

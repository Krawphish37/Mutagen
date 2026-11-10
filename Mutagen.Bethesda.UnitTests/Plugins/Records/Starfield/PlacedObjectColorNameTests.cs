using System.Drawing;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Two placed-object colour fields get xEdit's names:
// XPRM's 4th float (Unknown) is the Alpha of xEdit's RGBA colour (23,749 XPRM in Starfield.esm, all 32 bytes;
// the 4,390 with alpha 1 are exactly the 4,390 Line primitives), and XLCD's UnknownColor is xEdit's Color.
// Each test reads a whole reference from Starfield.esm with both readers and writes it back byte for byte.
public class PlacedObjectColorNameTests
{
    private static IEnumerable<IPlacedObjectGetter> Read(string hex)
        => ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(hex)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    private static void WritesBack(IPlacedObjectGetter refr, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // [no EditorID] (19E457:Starfield.esm), a box primitive
    private const string Primitive = "4E414D450400210000005850524D2000AD20F53F7A2D8B3E48D0AF3F0000803F0000803F000000009A99993E0100000058434F4C08003100000000000000444154411800905BA63D98AF213EF0C3B6BE000000000000000000000000";

    // [no EditorID] (1D2DB6:Starfield.esm), a light reference
    private const string LightColor = "424643422400424753436F6E74616374536861646F77436F6D706F6E656E745F436F6D706F6E656E7400464C43531400BC74133C0000404000002041CDCC4C3E8FC2753D4246434500004E414D450400FEEA04005852445304008D738EC0584C49472000000000007F94D73D00000000000000000000000000000000ABABABAB00ABABAB584C43440C00291AFAFF0000000000ABABAB584C5952040049F73B004441544118007662BC3F06580EC00E74A0BF000000000000000000000000";

    [Fact]
    public void Primitive_Alpha()
    {
        foreach (var refr in Read(Primitive))
        {
            var prim = refr.Primitive.ShouldNotBeNull();
            prim.Color.X.ShouldBe(1f);
            prim.Color.Y.ShouldBe(1f);
            prim.Color.Z.ShouldBe(0f);
            prim.Alpha.ShouldBe(0.3f, 0.0001f);
            prim.Type.ShouldBe(PlacedPrimitive.TypeEnum.Box);
            WritesBack(refr, Primitive);
        }
    }

    [Fact]
    public void LightColor_Color()
    {
        foreach (var refr in Read(LightColor))
        {
            refr.LightColors.ShouldNotBeNull().Single().Color.ShouldBe(Color.FromArgb(0xFF, 0x29, 0x1A, 0xFA));
            WritesBack(refr, LightColor);
        }
    }
}

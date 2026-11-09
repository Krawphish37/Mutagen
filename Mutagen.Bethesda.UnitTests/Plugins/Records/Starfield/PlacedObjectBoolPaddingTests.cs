using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Two placed object bools are one byte followed by 3 unused bytes (xEdit): XBSD Wind - Detached End and XLGD Is Animated.
// Mutagen read them as 4-byte bools (importantByteLength 1), which reads right but writes 01 00 00 00 / 00 00 00 00, so
// the filler is lost: XBSD ends in AB AB AB on 9,845 of 9,851 splines in Starfield.esm, XLGD on all 59.
// Now a 1-byte Bool and a 3-byte ...Unused array, written back byte for byte.
public class PlacedObjectBoolPaddingTests
{
    private static IEnumerable<IPlacedObjectGetter> Read(string sub, string hex)
        => ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord(sub, hex)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    private static void WritesBack(IPlacedObjectGetter refr, string sub, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
        written.FindSubrecord(new RecordType(sub)).Content.ToArray().ShouldBe(Hex(hex));
    }

    [Fact]
    public void Spline_WindDetachedEnd()
    {
        // XBSD of REFR 13A42A (Starfield.esm): wind-detached end set, then AB AB AB
        const string xbsd = "00-00-00-00-CD-CC-4C-3D-00-00-00-00-00-00-00-00-00-DD-58-3E-01-AB-AB-AB";
        foreach (var refr in Read("XBSD", xbsd))
        {
            refr.Spline!.IsWindDetachedEnd.ShouldBeTrue();
            refr.Spline.IsWindDetachedEndUnused.ToArray().ShouldBe(Hex("AB-AB-AB"));
            WritesBack(refr, "XBSD", xbsd);
        }
    }

    [Fact]
    public void Gobo_IsAnimated()
    {
        // XLGD of REFR 080EE1 (Starfield.esm): static scale U/V 2, animated, then AB AB AB
        const string xlgd = "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-40-00-00-00-40-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-AB-AB-AB";
        foreach (var refr in Read("XLGD", xlgd))
        {
            refr.GoboAnimatedProperties!.StaticScaleU.ShouldBe(2f);
            refr.GoboAnimatedProperties.IsAnimated.ShouldBeTrue();
            refr.GoboAnimatedProperties.IsAnimatedUnused.ToArray().ShouldBe(Hex("AB-AB-AB"));
            WritesBack(refr, "XLGD", xlgd);
        }
    }
}

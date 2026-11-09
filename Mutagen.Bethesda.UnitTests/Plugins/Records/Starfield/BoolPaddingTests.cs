using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// One-byte bools followed by unused bytes (xEdit) that Mutagen read as 2- or 4-byte bools (importantByteLength 1): the
// value read right, but the write put zeros over the padding. Now a 1-byte Bool plus a ...Unused byte array, as in the
// fixes chain (cae2b97266). Records in Starfield.esm whose padding is not zero (so they changed on write):
// - Ownership XOWN No Crime: REFR 18,018 of 35,412, CELL 65 of 65, PGRE 43 of 301, ACHR 34 of 75.
// - Light DAT2 Use Adaptive Lighting (1 unused byte): 622 of 1,575 LIGH.
// - LightRoundness Uses Roundness: REFR XLRD 61,967 of 61,974 (LIGH FLRD: none of 1,575).
// - LightGobo Is Animated (LIGH FLGD): 7 of 1,575.
// - Location LCEP enable points: all 134 LCTN with LCEP (3,313 of 3,459 items).
public class BoolPaddingTests
{
    private static byte[] Written<T>(T rec, Action<T, Mutagen.Bethesda.Plugins.Binary.Streams.MutagenWriter> write, string sub)
        => GameConstants.Starfield.MajorRecord(Write(w => write(rec, w))).FindSubrecord(new RecordType(sub)).Content.ToArray();

    [Fact]
    public void Ownership_NoCrime()
    {
        // XOWN of REFR 2F9A98 (Starfield.esm): owner 1D045D, No Crime, then A5 0C 00
        const string xown = "5D-04-1D-00-00-00-00-00-01-A5-0C-00";
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XOWN", xown)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.Ownership!.NoCrime.ShouldBeTrue();
            refr.Ownership.NoCrimeUnused.ToArray().ShouldBe(Hex("A5-0C-00"));
            Written(refr, (r, w) => r.WriteToBinary(w), "XOWN").ShouldBe(Hex(xown));
        }
    }

    [Fact]
    public void PlacedLight_UsesRoundness()
    {
        // XLRD of REFR 23BBF3 (Starfield.esm): uses roundness, then AB AB AB
        const string xlrd = "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-AB-AB-AB";
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XLRD", xlrd)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.LightRoundedness!.UsesRoundness.ShouldBeTrue();
            refr.LightRoundedness.UsesRoundnessUnused.ToArray().ShouldBe(Hex("AB-AB-AB"));
            Written(refr, (r, w) => r.WriteToBinary(w), "XLRD").ShouldBe(Hex(xlrd));
        }
    }

    [Fact]
    public void Light_AdaptiveLighting_And_Gobo()
    {
        // DAT2 of LGT_SpaceStation_Spot_SS_Cool_001_8k (0027B8:Starfield.esm): Use Adaptive Lighting 1, then AB
        const string dat2 = "FF-FF-FF-FF-00-00-00-41-C9-E2-FF-00-01-00-00-00-00-00-00-40-00-00-16-43-9A-99-19-3E-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-20-41-40-1F-00-00-01-00-01-AB-00-00-00-00-00-00-60-40-00-00-60-40-00-00-80-40";
        // FLGD of GenLightShadow1 (0116AE:Starfield.esm): not animated, then 02 00 00
        const string flgd = "00-00-00-00-00-00-00-00-00-00-00-3F-00-00-00-3F-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-80-3F-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-02-00-00";
        var bytes = Record("LIGH", Subrecord("DAT2", dat2), Subrecord("FLGD", flgd));
        foreach (var ligh in ReadBoth<ILightGetter>(bytes, f => Light.CreateFromBinary(f), (s, p) => LightBinaryOverlay.LightFactory(s, p)))
        {
            ligh.UseAdaptiveLighting.ShouldBeTrue();
            ligh.UseAdaptiveLightingUnused.ToArray().ShouldBe(Hex("AB"));
            ligh.GoboData.IsAnimated.ShouldBeFalse();
            ligh.GoboData.IsAnimatedUnused.ToArray().ShouldBe(Hex("02-00-00"));
            Written(ligh, (r, w) => r.WriteToBinary(w), "DAT2").ShouldBe(Hex(dat2));
            Written(ligh, (r, w) => r.WriteToBinary(w), "FLGD").ShouldBe(Hex(flgd));
        }
    }

    [Fact]
    public void Location_EnablePoints()
    {
        // First two LCEP items of CityNewAtlantisLocation (01295A:Starfield.esm): flag 1, then 31 FC 44
        const string lcep = "1C-FD-03-00-16-FD-03-00-01-31-FC-44-32-8A-13-00-15-FD-03-00-01-31-FC-44";
        foreach (var lctn in ReadBoth<ILocationGetter>(Record("LCTN", Subrecord("LCEP", lcep)), f => Location.CreateFromBinary(f), (s, p) => LocationBinaryOverlay.LocationFactory(s, p)))
        {
            var points = lctn.MasterEnablePointReferences!;
            points.Count.ShouldBe(2);
            points[0].SetEnableStateToOppositeOfParent.ShouldBeTrue();
            points[0].SetEnableStateToOppositeOfParentUnused.ToArray().ShouldBe(Hex("31-FC-44"));
            Written(lctn, (r, w) => r.WriteToBinary(w), "LCEP").ShouldBe(Hex(lcep));
        }
    }
}

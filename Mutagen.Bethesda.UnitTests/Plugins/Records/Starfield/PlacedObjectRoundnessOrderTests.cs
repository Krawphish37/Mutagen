using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// REFR XLRD (light roundness) has no single place in Starfield.esm: of 61,974 REFR with XLRD, 27,319 hold their
// subrecords in an order Mutagen's field order doesn't write, 3,296 of them only because of where XLRD is, and no other
// place for XLRD in PlacedObject.xml puts more records in order (each one leaves 478,556 or more REFR out of order).
// The kept subrecord order (SubrecordOrder) writes these back as they were. Found by agent b1 on REFR 2024F1.
public class PlacedObjectRoundnessOrderTests
{
    // REFR 2024F1 (Starfield.esm), a light: ... XLSM XLCD XLRD XWPK GNAM HNAM INAM JNAM LNAM XWPK XBPO DATA
    private const string Light = "4246434219004247534B6579776F7264466F726D5F436F6D706F6E656E74004B53495A0400010000004B574441040084D802004246434500004E414D450400BF1717005852445304006F9587C0584C49472000E007DEC076409E3F4AF733416A66E63E0000000000000000ABABABAB00ABABAB584C4244180000000000000000000000000000000000000000000000ABAB58414C44100000000000000000000000000000000000584C534D040001000000584C43440C000CD5F3FF0000000000ABABAB584C524414000000000000000000000000000000000001ABABAB5857504B0000474E414D040045530100484E414D0400AF672000494E414D020000004A4E414D0400455301004C4E414D0400000000005857504B00005842504F04000A000000444154411800D7C0D7BF4E5ACD40D0D8DA3EDA0FC93F00000080E4CB9640";

    [Fact]
    public void RoundnessBeforePackIn_WrittenBackByteForByte()
    {
        var bytes = Hex(Light);
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", bytes),
                     f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.LightRoundedness.ShouldNotBeNull();
            GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w))).Content.ToArray().ShouldBe(bytes);
        }
    }
}

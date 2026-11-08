using System.Drawing;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// REFR debug text format (XDTF) is Color (RGBA) then Size. Mutagen had Size first, so Size read a colour
// (4,278,190,080 for opaque black) and Color read the font size. All 175 XDTF in Starfield.esm have alpha 255 in the
// first 4 bytes and a small size (6-20) in the second.
public class DebugTextFormatTests
{
    // XDTS and XDTF of REFR 1D7D91 "Crew Area": xEdit Color 203, 239, 191, 255 and Size 8
    private const string CrewAreaText = "43-72-65-77-20-41-72-65-61-00";
    private const string CrewAreaFormat = "CB-EF-BF-FF-08-00-00-00";

    private static IEnumerable<IPlacedObjectGetter> ReadRef() =>
        ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XDTS", CrewAreaText), Subrecord("XDTF", CrewAreaFormat)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    [Fact]
    public void Format_Reads_Color_Then_Size()
    {
        foreach (var placed in ReadRef())
        {
            var format = placed.DebugText!.Format!;
            format.Size.ShouldBe(8u);
            format.Color.ToArgb().ShouldBe(Color.FromArgb(255, 203, 239, 191).ToArgb());
            placed.DebugText.String.ShouldBe("Crew Area");
        }
    }

    [Fact]
    public void Format_WritesSameBytes()
    {
        foreach (var placed in ReadRef())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XDTF")).Content.ToArray().ShouldBe(Hex(CrewAreaFormat));
        }
    }
}

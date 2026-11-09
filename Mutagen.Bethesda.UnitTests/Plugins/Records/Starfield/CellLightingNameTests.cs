using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Cell lighting (XCLL) names from xEdit's Starfield definitions: offset 28 is Gravity Scale (Mutagen had Fallout 4's
// DirectionalFade) and offset 56 is a colour (xEdit "Unknown", byte colours; Mutagen had the Int32 Unknown1).
// Starfield.esm, 11,985 cells with lighting: Gravity Scale 1 on 11,935, 0 on 38 (31 of them space cells), and 0.38,
// 0.61, 0.64, 0.95, 1.47 on interiors (0.38 is Mars: CityCydoniaMainLevel). The colour's 4th byte is 0 on all.
public class CellLightingNameTests
{
    // XCLL of CityCydoniaMainLevel (2B3DA2:Starfield.esm): Gravity Scale 0.38, colour 10 11 0A
    private const string Cydonia = "28-24-17-00-00-00-00-00-6F-54-40-00-00-00-00-00-00-80-3B-45-00-00-00-00-00-00-00-00-5C-8F-C2-3E-00-00-00-00-33-33-33-3F-2C-24-18-00-00-00-00-00-00-00-00-00-00-00-00-00-10-11-0A-00-00-00-00-00-00-00-00-00-80-06-00-00-63-5F-4B-00-33-33-33-3F-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-40-1C-46-00-00-00-00-00-00-00-00";

    [Fact]
    public void GravityScale_AndUnknownColor()
    {
        foreach (var cell in ReadBoth<ICellGetter>(Record("CELL", Subrecord("XCLL", Cydonia)), f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p)))
        {
            var l = cell.Lighting!;
            l.GravityScale.ShouldBe(0.38f);
            l.UnknownColor.R.ShouldBe((byte)0x10);
            l.UnknownColor.G.ShouldBe((byte)0x11);
            l.UnknownColor.B.ShouldBe((byte)0x0A);
            l.UnknownColor.A.ShouldBe((byte)0);
            var written = GameConstants.Starfield.MajorRecord(Write(w => cell.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XCLL")).Content.ToArray().ShouldBe(Hex(Cydonia));
        }
    }
}

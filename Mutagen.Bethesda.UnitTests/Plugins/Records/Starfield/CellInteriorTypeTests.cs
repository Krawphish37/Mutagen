using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield cell's lighting (XCLL) ends with Interior Type as one byte and 3 unused bytes (xEdit's Starfield
// definitions). The unused bytes hold filler (AB AB AB on 8,922 cells in Starfield.esm, EF EF EF, FF FF FF), so read
// as 4 bytes PackIn (3) became -1414812925 with no name on ~9,500 of 11,985 cells.
public class CellInteriorTypeTests
{
    // XCLL of cell 3BC26F in Starfield.esm: interior type 3 (PackIn) then AB AB AB
    private const string Lighting =
        "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-00-00-9F-07-00-00-00-00-00-00-00-40-1C-46-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-00-00-00-40-1C-46-03-AB-AB-AB";

    [Fact]
    public void PackIn_WithFiller_Reads()
    {
        var bytes = Record("CELL", Subrecord("XCLL", Lighting));
        foreach (var cell in ReadBoth<ICellGetter>(bytes, f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p)))
        {
            cell.Lighting!.InteriorType.ShouldBe(Cell.InteriorType.PackIn);
            cell.Lighting!.InteriorTypeUnused.ToArray().ShouldBe(new byte[] { 0xAB, 0xAB, 0xAB });
        }
    }

    [Fact]
    public void FillerBytes_Survive()
    {
        var cell = Cell.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
            new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenMemoryReadStream(Record("CELL", Subrecord("XCLL", Lighting)), Meta())));
        var written = GameConstants.Starfield.MajorRecord(Write(w => cell.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XCLL")).Content.ToArray().ShouldBe(Hex(Lighting));
    }
}

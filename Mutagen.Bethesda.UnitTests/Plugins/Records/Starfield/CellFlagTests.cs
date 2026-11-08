using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield cell flags (DATA) include Use Location as Name (bit 16), Use Planet Gravity (17), Use IS Volumes
// Interior Criteria (19) and Do Not Apply Biome Layered Material Swaps (20), per xEdit's Starfield
// definitions. Starfield.esm sets the first three on 136, 77 and 26 cells.
public class CellFlagTests
{
    private static IEnumerable<ICellGetter> ReadCell(string data) =>
        ReadBoth<ICellGetter>(Record("CELL", Subrecord("DATA", data)),
            f => Cell.CreateFromBinary(f),
            (s, p) => CellBinaryOverlay.CellFactory(s, p));

    [Fact]
    public void LocationNameAndPlanetGravity_Read()
    {
        // OEAF004Cave (CELL 236B89): DATA 0x00030005
        foreach (var cell in ReadCell("05-00-03-00"))
        {
            cell.Flags.ShouldBe(Cell.Flag.IsInteriorCell | Cell.Flag.CantTravelFromHere
                | Cell.Flag.UseLocationAsName | Cell.Flag.UsePlanetGravity);
        }
    }

    [Fact]
    public void ImageSpaceVolumesInteriorCriteria_Reads()
    {
        // LondinionExt01 (CELL 2BFC5C): DATA 0x00080002
        foreach (var cell in ReadCell("02-00-08-00"))
        {
            cell.Flags.ShouldBe(Cell.Flag.HasWater | Cell.Flag.UseImageSpaceVolumesInteriorCriteria);
        }
    }

    [Theory]
    [InlineData(Cell.Flag.UseLocationAsName, 0x0001_0000u)]
    [InlineData(Cell.Flag.UsePlanetGravity, 0x0002_0000u)]
    [InlineData(Cell.Flag.UseImageSpaceVolumesInteriorCriteria, 0x0008_0000u)]
    [InlineData(Cell.Flag.DoNotApplyBiomeLayeredMaterialSwaps, 0x0010_0000u)]
    public void Flag_Writes(Cell.Flag flag, uint expected)
    {
        var cell = new Cell(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { Flags = flag };
        var written = GameConstants.Starfield.MajorRecord(Write(w => cell.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("DATA")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}

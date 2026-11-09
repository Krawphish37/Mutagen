using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A cell's grid (XCLC) is X, Y, then Land Flags: one byte (xEdit: Hide Quad 1-4 = bits 0-3, No Collision Quad 1-4 = bits 4-7)
// and 3 unused bytes. Mutagen read the flags as a 4-byte enum with the values 0-3, so the filler in the unused bytes
// (different on every cell, e.g. FE 91 EB) made every value unnamed.
// Starfield.esm, 18,732 grids: Land Flags 0 on 18,717, 15 on 8, 255 on 5, 1 and 4 on one each.
public class CellGridLandFlagTests
{
    private static IEnumerable<ICellGetter> Read(string xclc)
        => ReadBoth<ICellGetter>(Record("CELL", Subrecord("XCLC", xclc)), f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p));

    private static void WritesBack(ICellGetter cell, string xclc)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => cell.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XCLC")).Content.ToArray().ShouldBe(Hex(xclc));
    }

    [Fact]
    public void AllQuadsHidden()
    {
        // XCLC of NewAtlantisSpaceport (0125B6:Starfield.esm): (-1, -1), Land Flags 0x0F
        const string xclc = "FF-FF-FF-FF-FF-FF-FF-FF-0F-FE-91-EB";
        foreach (var cell in Read(xclc))
        {
            cell.Grid!.Point.X.ShouldBe(-1);
            cell.Grid.Point.Y.ShouldBe(-1);
            cell.Grid.Flags.ShouldBe(CellGrid.Flag.HideQuad1 | CellGrid.Flag.HideQuad2 | CellGrid.Flag.HideQuad3 | CellGrid.Flag.HideQuad4);
            cell.Grid.Unused.ToArray().ShouldBe(Hex("FE-91-EB"));
            WritesBack(cell, xclc);
        }
    }

    [Fact]
    public void OneQuadHidden()
    {
        // XCLC of NewAtlantisShipHangar02 (0151ED:Starfield.esm): (-2, -3), Land Flags 0x04
        const string xclc = "FE-FF-FF-FF-FD-FF-FF-FF-04-7F-96-EC";
        foreach (var cell in Read(xclc))
        {
            cell.Grid!.Flags.ShouldBe(CellGrid.Flag.HideQuad3);
            WritesBack(cell, xclc);
        }
    }

    [Fact]
    public void AllFlags()
    {
        // XCLC of NeonGenerdyne (294F24:Starfield.esm): (1, 0), Land Flags 0xFF
        const string xclc = "01-00-00-00-00-00-00-00-FF-6E-D8-54";
        foreach (var cell in Read(xclc))
        {
            cell.Grid!.Point.X.ShouldBe(1);
            cell.Grid.Flags.HasFlag(CellGrid.Flag.NoCollisionQuad4).ShouldBeTrue();
            ((byte)cell.Grid.Flags).ShouldBe((byte)0xFF);
            WritesBack(cell, xclc);
        }
    }
}

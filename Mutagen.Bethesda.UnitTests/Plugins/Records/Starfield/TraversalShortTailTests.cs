using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C191: XTV2 ends in zero "fluff" bytes (NumTraversalFluffBytes). When fewer were left than an entry needs (0x34, or
// 0x38 with a FormID), the readers read an entry anyway, past XTV2's end: into the next subrecord (REFR: XSCL, DATA...)
// or, for an uncompressed CELL, into its children's group header. The full reader then kept a garbage traversal and
// lost the fluff; the overlay did the same when the bytes after XTV2 weren't zero. Real CELL tails of 4-48 bytes
// (Starfield.esm 34, ShatteredSpace 7) read right only because XTV2 is the record's last subrecord there and the
// over-read hit the end of a compressed record. Both readers now count a tail too short for an entry as fluff.
public class TraversalShortTailTests
{
    public static TheoryData<uint> Tails() => new() { 4, 8, 36, 51, 54 };

    private static TraversalReference Entry(float x) => new()
    {
        Unknown1 = 1,
        From = new P3Float(x, 2, 3),
        To = new P3Float(4, 5, 6),
    };

    [Theory]
    [MemberData(nameof(Tails))]
    public void ShortZeroTailIsFluff_CellAndReference_BothReaders(uint tail)
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("ShortTail.esp"), StarfieldRelease.Starfield);
        var placed = new PlacedObject(mod)
        {
            EditorID = "Placed",
            Traversals = new ExtendedList<TraversalReference> { Entry(1) },
            NumTraversalFluffBytes = tail,
            Scale = 2.5f, // XSCL follows XTV2
        };
        var cell = new Cell(mod)
        {
            EditorID = "TestCell",
            Traversals = new ExtendedList<TraversalReference> { Entry(1), Entry(2) },
            NumTraversalFluffBytes = tail,
        };
        cell.Temporary.Add(placed); // the children's group follows the cell record
        mod.Cells.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } },
        });

        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        foreach (var (read, reader) in new IStarfieldModGetter[] { full, overlay }.Zip(new[] { "full", "overlay" }))
        {
            var readCell = read.Cells.Records.Single().SubBlocks.Single().Cells.Single();
            $"{reader} {readCell.Traversals!.Count} {readCell.NumTraversalFluffBytes}".ShouldBe($"{reader} 2 {tail}");
            readCell.Traversals.Select(t => t.From.X).ShouldBe([1f, 2f]);
            var readPlaced = (IPlacedObjectGetter)readCell.Temporary.Single();
            $"{reader} {readPlaced.Traversals!.Count} {readPlaced.NumTraversalFluffBytes}".ShouldBe($"{reader} 1 {tail}");
            readPlaced.Scale.ShouldBe(2.5f);
        }

        using var tmp2 = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        full.BeginWrite.ToPath(new ModPath(mod.ModKey, tmp2.File.Path)).WithNoLoadOrder().NoModKeySync().Write();
        File.ReadAllBytes(tmp2.File.Path).ShouldBe(File.ReadAllBytes(tmp.File.Path));
    }
}

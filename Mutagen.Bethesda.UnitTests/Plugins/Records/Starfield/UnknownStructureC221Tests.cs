using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C221: types for unknown fields that real data settles (b1's unknown-field profile, C208), names unchanged:
// - PlacedObjectVolumeData.Unknown1 (XVL2 @0) is a link to an image space adapter (IMAD);
// - AreaLight.Unused is a half-precision float;
// - TraversalReference's 8 bytes after the traversal FormID are four half-precision floats (Unknown2-Unknown5).
// Halves are read and written by their bits, and compared by their bits (NaN payloads, -0, subnormals, infinities).
public class UnknownStructureC221Tests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("C221.esp");

    // odd bit patterns: zeros, infinities, quiet and signalling NaNs with payloads, subnormals, round values, fill bytes
    private static readonly ushort[] OddHalves =
        [0x0000, 0x8000, 0x7C00, 0xFC00, 0x7E00, 0x7D01, 0xFFFF, 0xFE01, 0x0001, 0x83FF, 0x3C00, 0xBE00, 0x3D00, 0xABAB, 0x7BFF];

    private sealed class Written : IDisposable
    {
        private readonly TempFile _tmp;
        public byte[] Bytes { get; }
        public StarfieldMod Full { get; }
        public IStarfieldModDisposableGetter Overlay { get; }

        public Written(StarfieldMod mod)
        {
            _tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
            var path = new ModPath(mod.ModKey, _tmp.File.Path);
            mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
            Bytes = File.ReadAllBytes(_tmp.File.Path);
            Full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
            Overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        }

        public IEnumerable<T> Both<T>(FormKey formKey) where T : IMajorRecordGetter =>
            new IStarfieldModGetter[] { Full, Overlay }
                .Select(m => (T)m.EnumerateMajorRecords().Single(r => r.FormKey == formKey));

        public void Dispose()
        {
            Overlay.Dispose();
            _tmp.Dispose();
        }
    }

    private static StarfieldMod NewMod() => new(TestModKey, StarfieldRelease.Starfield);

    private static void AddInteriorCell(StarfieldMod mod, Cell cell) =>
        mod.Cells.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } },
        });

    private static ushort Bits(Half h) => BitConverter.HalfToUInt16Bits(h);
    private static Half H(ushort bits) => BitConverter.UInt16BitsToHalf(bits);

    // ---- the Half type ----

    [Fact]
    public void HalvesCompareAndHashByTheirBits()
    {
        HalfBinaryTranslation.BitEquals(H(0x0000), H(0x8000)).ShouldBeFalse(); // Half's == says +0 == -0
        HalfBinaryTranslation.BitEquals(H(0x7E00), H(0x7E00)).ShouldBeTrue();  // Half's == says NaN != NaN
        HalfBinaryTranslation.BitEquals(H(0x7E00), H(0x7E01)).ShouldBeFalse(); // NaN payloads differ
        HalfBinaryTranslation.BitHash(H(0x7E00)).ShouldBe(HalfBinaryTranslation.BitHash(H(0x7E00)));

        var a = new AreaLight { Unused = H(0x0000) };
        var b = new AreaLight { Unused = H(0x8000) };
        a.Equals(b).ShouldBeFalse();
        var n1 = new AreaLight { Unused = H(0x7D01) };
        var n2 = new AreaLight { Unused = H(0x7D01) };
        n1.Equals(n2).ShouldBeTrue();
        n1.GetHashCode().ShouldBe(n2.GetHashCode());
        n1.Equals(new AreaLight { Unused = H(0x7D02) }).ShouldBeFalse();
    }

    [Fact]
    public void AreaLightHalfComesBackBitForBit()
    {
        var mod = NewMod();
        var keys = new List<(FormKey Key, ushort Bits)>();
        foreach (var bits in OddHalves)
        {
            var light = new Light(mod) { EditorID = $"L{bits:X4}", AreaLight = new AreaLight { Width = 1, Unused = H(bits) } };
            mod.Lights.Add(light);
            keys.Add((light.FormKey, bits));
        }
        using var written = new Written(mod);
        foreach (var (key, bits) in keys)
        {
            foreach (var read in written.Both<ILightGetter>(key))
            {
                Bits(read.AreaLight.Unused).ShouldBe(bits);
            }
        }
        // and through a full import, written again: the same file
        using var again = new Written(written.Full);
        again.Bytes.ShouldBe(written.Bytes);
    }

    [Fact]
    public void TraversalHalvesComeBackBitForBitInBothReaders()
    {
        var mod = NewMod();
        var traversals = new ExtendedList<TraversalReference>();
        for (int i = 0; i + 3 < OddHalves.Length; i += 2)
        {
            traversals.Add(new TraversalReference
            {
                Unknown1 = i + 1,
                From = new P3Float(1, 2, 3),
                To = new P3Float(4, 5, 6),
                Unknown2 = H(OddHalves[i]),
                Unknown3 = H(OddHalves[i + 1]),
                Unknown4 = H(OddHalves[i + 2]),
                Unknown5 = H(OddHalves[i + 3]),
            });
        }
        // one with a traversal FormID, so the halves sit 4 bytes later
        traversals[0].Traversal.SetTo(new FormKey(TestModKey, 0x900));
        var cell = new Cell(mod) { EditorID = "TestCell", Traversals = traversals };
        AddInteriorCell(mod, cell);
        using var written = new Written(mod);
        foreach (var read in written.Both<ICellGetter>(cell.FormKey))
        {
            read.Traversals!.Count.ShouldBe(traversals.Count);
            for (int i = 0; i < traversals.Count; i++)
            {
                var t = read.Traversals[i];
                new[] { t.Unknown2, t.Unknown3, t.Unknown4, t.Unknown5 }.Select(Bits).ShouldBe(
                    new[] { traversals[i].Unknown2, traversals[i].Unknown3, traversals[i].Unknown4, traversals[i].Unknown5 }.Select(Bits));
            }
        }
        using var again = new Written(written.Full);
        again.Bytes.ShouldBe(written.Bytes);
    }

    // ---- the IMAD link ----

    [Fact]
    public void VolumeDataUnknown1IsAnImageSpaceAdapterLink()
    {
        var mod = NewMod();
        var imad = new ImageSpaceAdapter(mod) { EditorID = "TestIMAD" };
        mod.ImageSpaceAdapters.Add(imad);
        var placed = new PlacedObject(mod) { EditorID = "Volume", VolumeData = new PlacedObjectVolumeData() };
        placed.VolumeData.Unknown1.SetTo(imad);
        var cell = new Cell(mod) { EditorID = "TestCell" };
        cell.Temporary.Add(placed);
        AddInteriorCell(mod, cell);

        placed.EnumerateFormLinks().Select(l => l.FormKey).ShouldContain(imad.FormKey);
        placed.EnumerateFormLinks().Single(l => l.FormKey == imad.FormKey).Type.ShouldBe(typeof(IImageSpaceAdapterGetter));

        using var written = new Written(mod);
        foreach (var read in written.Both<IPlacedObjectGetter>(placed.FormKey))
        {
            read.VolumeData!.Unknown1.FormKey.ShouldBe(imad.FormKey);
            read.EnumerateFormLinks().Select(l => l.FormKey).ShouldContain(imad.FormKey);
        }

        // remapped like any link
        var other = new FormKey(ModKey.FromNameAndExtension("Other.esm"), 0x123);
        placed.RemapLinks(new Dictionary<FormKey, FormKey> { [imad.FormKey] = other });
        placed.VolumeData.Unknown1.FormKey.ShouldBe(other);
    }
}

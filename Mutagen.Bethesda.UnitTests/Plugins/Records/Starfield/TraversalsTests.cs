using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.Testing.Extensions;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class TraversalsTests
{
    [Fact]
    public void EdgeCaseMutable()
    {
        var overlayStream = TestDataPathing.GetReadFrame(
            TestDataPathing.StarfieldTraversals, 
            GameRelease.Starfield);
        var obj = Cell.CreateFromBinary(
            overlayStream);
        obj.Temporary.ShouldHaveCount(2);
        // obj.Traversals.ShouldHaveCount(3);
    }
    
    [Fact]
    public void EdgeCaseOverlay()
    {
        var overlayStream = TestDataPathing.GetOverlayStream(
            TestDataPathing.StarfieldTraversals, 
            GameRelease.Starfield);
        var obj = CellBinaryOverlay.CellFactory(
            overlayStream,
            new BinaryOverlayFactoryPackage(overlayStream.MetaData));
        obj.Temporary.ShouldHaveCount(2);
        // obj.Traversals.ShouldHaveCount(3);
    }

    private static readonly ModKey TestModKey = ModKey.FromFileName("Test.esm");

    private static OverlayStream GetStream(byte[] data)
    {
        return new OverlayStream(data, new ParsingMeta(
            GameConstants.Starfield,
            TestModKey,
            SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey))));
    }

    private static void WriteEntry(MutagenWriter writer, int unknown1, bool hasTraversal)
    {
        writer.Write(unknown1);
        for (int i = 0; i < 9; i++)
        {
            // From, To, UnknownVector
            writer.Write(1f + i);
        }
        if (hasTraversal)
        {
            writer.Write(0);
            writer.Write(0x123456);
        }
        else
        {
            writer.Write(TraversalReferenceBinaryCreateTranslation.HasNoTraversal);
        }
        writer.Write(0x0102030405060708L);
    }

    private static byte[] GetEntries(int count, int fluffLength)
    {
        var data = new MemoryTributary();
        using (var writer = new MutagenWriter(data, GameConstants.Starfield))
        {
            for (int i = 0; i < count; i++)
            {
                WriteEntry(writer, unknown1: i + 1, hasTraversal: i % 2 == 0);
            }
            writer.WriteZeros(checked((uint)fluffLength));
        }
        return data.ToArray();
    }

    private static IReadOnlyList<int> GetLocs(OverlayStream stream, long finalPos, out uint fluffBytes)
    {
        return TraversalReferenceBinaryOverlay.GetLocs(
            stream,
            new BinaryOverlayFactoryPackage(stream.MetaData),
            finalPos: finalPos,
            offset: 0,
            lastParsed: default,
            out fluffBytes);
    }

    [Fact]
    public void GetLocsMixedEntriesAndFluff()
    {
        var data = GetEntries(count: 3, fluffLength: 0x20);
        var stream = GetStream(data);
        var locs = GetLocs(stream, finalPos: data.Length, out var fluffBytes);
        locs.ShouldBe(new[] { 0, 0x38, 0x38 + 0x34 });
        fluffBytes.ShouldBe(0x20u);
        stream.Position.ShouldBe(data.Length);
    }

    [Fact]
    public void GetLocsNoFluff()
    {
        var data = GetEntries(count: 2, fluffLength: 0);
        var stream = GetStream(data);
        var locs = GetLocs(stream, finalPos: data.Length, out var fluffBytes);
        locs.ShouldBe(new[] { 0, 0x38 });
        fluffBytes.ShouldBe(0u);
        stream.Position.ShouldBe(data.Length);
    }

    [Fact]
    public void GetLocsAllFluff()
    {
        var data = new byte[0x100];
        var stream = GetStream(data);
        var locs = GetLocs(stream, finalPos: data.Length, out var fluffBytes);
        locs.ShouldBeEmpty();
        fluffBytes.ShouldBe(0x100u);
        stream.Position.ShouldBe(data.Length);
    }

    [Fact]
    public void GetLocsEmpty()
    {
        var data = new byte[0x10];
        var stream = GetStream(data);
        stream.Position = 0x10;
        var locs = GetLocs(stream, finalPos: 0x10, out var fluffBytes);
        locs.ShouldBeEmpty();
        fluffBytes.ShouldBe(0u);
    }

    [Fact]
    public void GetLocsDoesNotAllocatePerEntry()
    {
        const int count = 1000;
        var data = GetEntries(count: count, fluffLength: 0x40);
        var stream = GetStream(data);
        var package = new BinaryOverlayFactoryPackage(stream.MetaData);
        TraversalReferenceBinaryOverlay.GetLocs(stream, package, data.Length, 0, default, out _);
        stream.Position = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var locs = TraversalReferenceBinaryOverlay.GetLocs(stream, package, data.Length, 0, default, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        locs.Count.ShouldBe(count);
        // Only the location list itself should be allocated, nothing per entry
        allocated.ShouldBeLessThan(count * 16);
    }

    [Fact]
    public void OverlayFactory()
    {
        var payload = GetEntries(count: 3, fluffLength: 0x14);
        var data = new MemoryTributary();
        using (var writer = new MutagenWriter(data, GameConstants.Starfield))
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.XTV2))
            {
                writer.Write(payload);
            }
        }
        var bytes = data.ToArray();
        var stream = GetStream(bytes);
        var traversals = TraversalReferenceBinaryOverlay.Factory(
            stream,
            new BinaryOverlayFactoryPackage(stream.MetaData),
            finalPos: bytes.Length,
            offset: 0,
            lastParsed: default,
            out var fluffBytes);
        fluffBytes.ShouldBe(0x14u);
        traversals.Select(x => x.Unknown1).ShouldBe(new[] { 1, 2, 3 });
        var traversal = new FormKey(TestModKey, 0x123456);
        traversals.Select(x => x.Traversal.FormKeyNullable).ShouldBe(new FormKey?[] { traversal, null, traversal });
        traversals.Select(x => x.From.X).ShouldBe(new[] { 1f, 1f, 1f });
        traversals.Select(x => x.UnknownVector.Z).ShouldBe(new[] { 9f, 9f, 9f });
        traversals.Select(x => x.Unknown.ToArray()).ShouldAllBe(x => x.SequenceEqual(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }));
        stream.Position.ShouldBe(bytes.Length);
    }
}
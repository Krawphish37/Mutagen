using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class TraversalReferenceFlagsTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] Entry(int unknown1, int flags, uint? formId, byte unknownSeed)
    {
        var entry = new MemoryStream();
        entry.Write(BitConverter.GetBytes(unknown1));
        for (int i = 0; i < 9; i++)
        {
            entry.Write(BitConverter.GetBytes(unknown1 * 10f + i));
        }
        entry.Write(BitConverter.GetBytes(flags));
        if (formId.HasValue)
        {
            entry.Write(BitConverter.GetBytes(formId.Value));
        }
        for (int i = 0; i < 8; i++)
        {
            entry.WriteByte(checked((byte)(unknownSeed + i)));
        }
        return entry.ToArray();
    }

    private static byte[] MakePlacedObjectBytes(byte[] xtv2)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestRef\0"));
        WriteSubrecord(content, "XTV2", xtv2);
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("REFR"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IPlacedObjectGetter Read(byte[] bytes, bool overlay)
    {
        var meta = Meta();
        if (overlay)
        {
            return PlacedObjectBinaryOverlay.PlacedObjectFactory(
                new OverlayStream(bytes, meta),
                new BinaryOverlayFactoryPackage(meta));
        }
        return PlacedObject.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, meta)));
    }

    private static byte[] Write(IPlacedObjectGetter placed)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            placed.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static byte[] Subrecord(byte[] bytes, string type)
    {
        var target = Encoding.ASCII.GetBytes(type);
        for (int i = 24; i + 6 <= bytes.Length; )
        {
            var len = BitConverter.ToUInt16(bytes, i + 4);
            if (bytes.AsSpan(i, 4).SequenceEqual(target))
            {
                return bytes.AsSpan(i + 6, len).ToArray();
            }
            i += 6 + len;
        }
        throw new KeyNotFoundException(type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtraFlagsOnLinkedEntries_RoundTrip(bool overlay)
    {
        var xtv2 = Entry(1, 2, 0x801, 0x10)
            .Concat(Entry(2, 1, 0x802, 0x20))
            .Concat(Entry(3, 4, null, 0x30))
            .ToArray();

        var placed = Read(MakePlacedObjectBytes(xtv2), overlay);

        placed.Traversals.ShouldNotBeNull();
        placed.Traversals.Select(x => x.Traversal.FormKeyNullable).ShouldBe(new FormKey?[]
        {
            new FormKey(TestModKey, 0x801),
            new FormKey(TestModKey, 0x802),
            null,
        });
        Subrecord(Write(placed), "XTV2").ShouldBe(xtv2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoTraversalBitWithExtraFlags_ReadsWithoutFormID(bool overlay)
    {
        var xtv2 = Entry(1, 6, null, 0x10)
            .Concat(Entry(2, 0, 0x802, 0x20))
            .ToArray();

        var placed = Read(MakePlacedObjectBytes(xtv2), overlay);

        placed.Traversals.ShouldNotBeNull();
        placed.NumTraversalFluffBytes.ShouldBe(0u);
        placed.Traversals.Select(x => x.Traversal.FormKeyNullable).ShouldBe(new FormKey?[]
        {
            null,
            new FormKey(TestModKey, 0x802),
        });
        placed.Traversals[0].Unknown.ToArray().ShouldBe(new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17 });
        Subrecord(Write(placed), "XTV2").ShouldBe(xtv2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flags_ExcludeImplicitNoTraversalBit(bool overlay)
    {
        var xtv2 = Entry(1, 2, 0x801, 0x10)
            .Concat(Entry(2, 6, null, 0x20))
            .Concat(Entry(3, 4, null, 0x30))
            .ToArray();

        var placed = Read(MakePlacedObjectBytes(xtv2), overlay);

        placed.Traversals.ShouldNotBeNull();
        placed.Traversals.Select(x => x.Flags).ShouldBe(new[]
        {
            (TraversalReference.Flag)2,
            (TraversalReference.Flag)2,
            (TraversalReference.Flag)0,
        });
    }

    [Fact]
    public void Write_NoTraversalBitFollowsTraversal()
    {
        var placed = new PlacedObject(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Traversals = new ExtendedList<TraversalReference>()
            {
                new()
                {
                    Unknown1 = 1,
                    From = new P3Float(10, 11, 12),
                    Flags = (TraversalReference.Flag)2 | TraversalReference.Flag.NoTraversalFormID,
                    Unknown = new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17 },
                },
                new()
                {
                    Unknown1 = 2,
                    From = new P3Float(20, 21, 22),
                    Flags = (TraversalReference.Flag)1,
                    Unknown = new byte[] { 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27 },
                },
            }
        };
        placed.Traversals[0].Traversal.SetTo(new FormKey(TestModKey, 0x801));
        placed.Traversals[1].Traversal.SetToNull();

        var xtv2 = Subrecord(Write(placed), "XTV2");

        BitConverter.ToInt32(xtv2, 0x28).ShouldBe(2);
        BitConverter.ToUInt32(xtv2, 0x2C).ShouldBe(0x801u);
        BitConverter.ToInt32(xtv2, 0x38 + 0x28).ShouldBe(5);
        xtv2.Length.ShouldBe(0x38 + 0x34);
    }
}

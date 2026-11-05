using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class VolumesComponentTests
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

    // Type, four 4x4 matrices, three floats, then the type specific ender floats
    private static byte[] Item(int type, params float[] ender)
    {
        var ret = new MemoryStream();
        ret.Write(BitConverter.GetBytes(type));
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                ret.Write(BitConverter.GetBytes(row == col ? 1f : 0f));
            }
        }
        ret.Write(BitConverter.GetBytes(1f));
        ret.Write(BitConverter.GetBytes(1f));
        ret.Write(BitConverter.GetBytes(1f));
        foreach (var f in ender)
        {
            ret.Write(BitConverter.GetBytes(f));
        }
        return ret.ToArray();
    }

    private static byte[] Vlms(params byte[][] items)
    {
        return BitConverter.GetBytes(items.Length).Concat(items.SelectMany(x => x)).ToArray();
    }

    private static byte[] MakePlacedObjectBytes(byte[] vlms)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "BFCB", Encoding.ASCII.GetBytes("Volumes_Component\0"));
        WriteSubrecord(content, "VLMS", vlms);
        WriteSubrecord(content, "BFCE", Array.Empty<byte>());
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

    private static IPlacedObjectGetter ReadDirect(byte[] bytes)
    {
        return PlacedObject.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IPlacedObjectGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return PlacedObjectBinaryOverlay.PlacedObjectFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IPlacedObjectGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
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

    private static byte[] VlmsContent(byte[] bytes)
    {
        return GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(RecordTypes.VLMS).Content.ToArray();
    }

    private static IReadOnlyList<IVolumesComponentItemGetter> Items(IPlacedObjectGetter placed)
    {
        return placed.Components.OfType<IVolumesComponentGetter>().Single().Items;
    }

    [Fact]
    public void Type11_EnderValueReadAndRoundTrips()
    {
        var vlms = Vlms(Item(11, 2.5f));
        var bytes = MakePlacedObjectBytes(vlms);

        foreach (var placed in ReadBoth(bytes))
        {
            var item = Items(placed).ShouldHaveSingleItem();
            item.Ender.ShouldBeAssignableTo<IVolumesUnknownEnderEmptyPaddedGetter>()!
                .Unknown1.ShouldBe(BitConverter.SingleToUInt32Bits(2.5f));
            item.Unknown3.ShouldBe(1f);
            VlmsContent(Write(placed)).ShouldBe(vlms);
        }
    }

    [Fact]
    public void Type11_FollowedByAnotherItem_ReadsBoth()
    {
        var vlms = Vlms(Item(11, 0f), Item(1, 2.5f));
        var bytes = MakePlacedObjectBytes(vlms);

        foreach (var placed in ReadBoth(bytes))
        {
            var items = Items(placed);
            items.Count.ShouldBe(2);
            items[0].Ender.ShouldBeAssignableTo<IVolumesUnknownEnderEmptyPaddedGetter>()!
                .Unknown1.ShouldBe(0u);
            items[1].Ender.ShouldBeAssignableTo<IVolumesUnknownEnderSingleGetter>()!
                .Unknown1.ShouldBe(2.5f);
            VlmsContent(Write(placed)).ShouldBe(vlms);
        }
    }

    [Fact]
    public void Type11_WithoutValue_ReadAndRoundTrips()
    {
        var vlms = Vlms(Item(11));
        vlms.Length.ShouldBe(84);
        var bytes = MakePlacedObjectBytes(vlms);

        foreach (var placed in ReadBoth(bytes))
        {
            var item = Items(placed).ShouldHaveSingleItem();
            item.Ender.ShouldBeAssignableTo<IVolumesUnknownEnderEmptyGetter>();
            VlmsContent(Write(placed)).ShouldBe(vlms);
        }
    }

    [Fact]
    public void Type11_WithoutValue_FollowedByAnotherItem_RoundTrips()
    {
        var vlms = Vlms(Item(11), Item(1, 2.5f));
        var bytes = MakePlacedObjectBytes(vlms);

        foreach (var placed in ReadBoth(bytes))
        {
            var items = Items(placed);
            items.Count.ShouldBe(2);
            items[0].Ender.ShouldBeAssignableTo<IVolumesUnknownEnderEmptyGetter>();
            items[1].Ender.ShouldBeAssignableTo<IVolumesUnknownEnderSingleGetter>()!
                .Unknown1.ShouldBe(2.5f);
            VlmsContent(Write(placed)).ShouldBe(vlms);
        }
    }

    [Fact]
    public void Type11_BothLengthsInOneList_RoundTrips()
    {
        var vlms = Vlms(Item(11, 0f), Item(11), Item(5, 4f, 5f, 6f));
        var bytes = MakePlacedObjectBytes(vlms);

        foreach (var placed in ReadBoth(bytes))
        {
            var items = Items(placed);
            items.Count.ShouldBe(3);
            items[0].Ender.ShouldBeAssignableTo<IVolumesUnknownEnderEmptyPaddedGetter>();
            items[1].Ender.ShouldBeAssignableTo<IVolumesUnknownEnderEmptyGetter>();
            VlmsContent(Write(placed)).ShouldBe(vlms);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    [InlineData(11, 1)]
    public void EachType_FollowedByAnotherItem_RoundTrips(int type, int enderFloats)
    {
        var vlms = Vlms(
            Item(type, Enumerable.Range(1, enderFloats).Select(x => x * 0.5f).ToArray()),
            Item(5, 4f, 5f, 6f));
        var bytes = MakePlacedObjectBytes(vlms);

        foreach (var placed in ReadBoth(bytes))
        {
            Items(placed).Count.ShouldBe(2);
            VlmsContent(Write(placed)).ShouldBe(vlms);
        }
    }
}

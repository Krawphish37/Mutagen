using System.Text;
using Mutagen.Bethesda.Plugins;
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

// C50: each DAT2 of a PERS is one Items entry. An XXXX gives the length of the DAT2 right after it only; the reader kept
// that length (or the first DAT2's own) and read every later DAT2 with it, so a PERS whose DAT2s differ in length was
// misread. Both readers, and written back byte for byte.
public class PersDat2LengthTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        if (content.Length > ushort.MaxValue)
        {
            // as Mutagen writes it: XXXX with the length, then the subrecord with length 0
            stream.Write(Encoding.ASCII.GetBytes("XXXX"));
            stream.Write(BitConverter.GetBytes((ushort)4));
            stream.Write(BitConverter.GetBytes(content.Length));
            stream.Write(Encoding.ASCII.GetBytes(type));
            stream.Write(BitConverter.GetBytes((ushort)0));
        }
        else
        {
            stream.Write(Encoding.ASCII.GetBytes(type));
            stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        }
        stream.Write(content);
    }

    // count items: FormID 0x800 + i, Unknown1 i, Unknown2 base + i
    private static byte[] Items(int count, uint @base) =>
        Enumerable.Range(0, count)
            .SelectMany(i => BitConverter.GetBytes(0x800u + (uint)i)
                .Concat(BitConverter.GetBytes((uint)i))
                .Concat(BitConverter.GetBytes(@base + (uint)i)))
            .ToArray();

    private static byte[] MakePers(params byte[][] dat2s)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestPers\0"));
        WriteSubrecord(content, "DATA", BitConverter.GetBytes(7u));
        foreach (var dat2 in dat2s) WriteSubrecord(content, "DAT2", dat2);
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("PERS"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<IPERSGetter> ReadBoth(byte[] bytes)
    {
        yield return PERS.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return PERSBinaryOverlay.PERSFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] Write(IPERSGetter pers)
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
            pers.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static void ShouldHold(IPERSGetter pers, params (int Count, uint Base)[] expected)
    {
        pers.Items.Count.ShouldBe(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            var items = pers.Items[i].Items;
            items.Count.ShouldBe(expected[i].Count);
            for (int j = 0; j < items.Count; j++)
            {
                items[j].Record.FormKey.ShouldBe(new FormKey(TestModKey, 0x800u + (uint)j));
                items[j].Unknown1.ShouldBe((uint)j);
                items[j].Unknown2.ShouldBe(expected[i].Base + (uint)j);
            }
        }
    }

    [Fact]
    public void Dat2sOfDifferentLengths_EachReadWithItsOwn()
    {
        var bytes = MakePers(Items(2, 100), Items(3, 200), Items(1, 300));
        foreach (var pers in ReadBoth(bytes))
        {
            ShouldHold(pers, (2, 100), (3, 200), (1, 300));
            Write(pers).ShouldBe(bytes);
        }
    }

    [Fact]
    public void XxxxGivesTheLengthOfTheNextDat2Only()
    {
        // 5,462 items: 65,544 bytes, over a subrecord's 16-bit length, so the first DAT2 follows an XXXX
        var bytes = MakePers(Items(5462, 1000), Items(2, 100), Items(5462, 2000), Items(4, 200));
        foreach (var pers in ReadBoth(bytes))
        {
            ShouldHold(pers, (5462, 1000), (2, 100), (5462, 2000), (4, 200));
            Write(pers).ShouldBe(bytes);
        }
    }
}

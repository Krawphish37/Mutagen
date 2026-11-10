using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class DialogResponseTroiTests
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

    private static byte[] ZString(string str) => Encoding.ASCII.GetBytes(str + "\0");

    private static readonly byte[] Hash1 = { 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly byte[] Hash2 = { 9, 10, 11, 12, 13, 14, 15, 16 };

    private static void WriteResponse(MemoryStream content, string text, byte[] hash, uint? troi)
    {
        var trda = new MemoryStream();
        trda.Write(BitConverter.GetBytes(0xFFFFFFFF));
        trda.Write(BitConverter.GetBytes(0u));
        trda.Write(BitConverter.GetBytes(0f));
        WriteSubrecord(content, "TRDA", trda.ToArray());
        if (troi.HasValue)
        {
            WriteSubrecord(content, "TROI", BitConverter.GetBytes(troi.Value));
        }
        WriteSubrecord(content, "NAM1", ZString(text));
        WriteSubrecord(content, "NAM2", ZString(string.Empty));
        WriteSubrecord(content, "NAM3", ZString(string.Empty));
        WriteSubrecord(content, "NAM4", ZString(string.Empty));
        WriteSubrecord(content, "NAM9", hash);
    }

    // Layout written by the Creation Kit at form version 576: a TROI (FormID of another INFO)
    // directly after the TRDA of a response.
    private static byte[] MakeInfoBytes()
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "ENAM", new byte[4]);
        WriteResponse(content, "First line", Hash1, troi: null);
        WriteResponse(content, "Second line", Hash2, troi: 0x802);
        WriteSubrecord(content, "NAM0", ZString(string.Empty));
        WriteSubrecord(content, "INAM", BitConverter.GetBytes(0));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("INFO"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x801));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)576));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IDialogResponsesGetter ReadDirect(byte[] bytes)
    {
        return DialogResponses.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IDialogResponsesGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return DialogResponsesBinaryOverlay.DialogResponsesFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IDialogResponsesGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static byte[] Write(IDialogResponsesGetter info)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters)
        };
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, bundle, dispose: false))
        {
            info.WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    [Fact]
    public void TroiAfterTrda_KeepsResponseTextAndFollowingSubrecords()
    {
        var bytes = MakeInfoBytes();

        foreach (var info in ReadBoth(bytes))
        {
            info.Responses.Count.ShouldBe(2);

            var first = info.Responses[0];
            first.TROI.IsNull.ShouldBeTrue();
            first.ResponseText.String.ShouldBe("First line");
            first.CreationTime.ShouldBe(BitConverter.ToUInt64(Hash1));

            var second = info.Responses[1];
            second.TROI.FormKeyNullable.ShouldBe(new FormKey(TestModKey, 0x802));
            second.ResponseText.String.ShouldBe("Second line");
            second.CreationTime.ShouldBe(BitConverter.ToUInt64(Hash2));
        }
    }

    [Fact]
    public void TroiAfterTrda_RoundTrips()
    {
        var bytes = MakeInfoBytes();

        foreach (var info in ReadBoth(bytes))
        {
            Write(info).ShouldBe(bytes);
        }
    }
}

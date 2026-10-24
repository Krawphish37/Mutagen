using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Fallout3;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout3;

public class MissingDataSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Fallout3, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    // A record with only an EDID, so its DATA subrecord is missing
    private static byte[] MakeRecordBytes(string recordType)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestRecord\0"));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(recordType));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)15));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static MutagenFrame DirectFrame(byte[] bytes) => new(new MutagenMemoryReadStream(bytes, Meta()));

    private static (OverlayStream Stream, BinaryOverlayFactoryPackage Package) OverlayArgs(byte[] bytes)
    {
        var meta = Meta();
        return (new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void Class_OverlayReadsDataFieldsAsDefaults()
    {
        var bytes = MakeRecordBytes("CLAS");
        var direct = Class.CreateFromBinary(DirectFrame(bytes));
        var (stream, package) = OverlayArgs(bytes);
        var overlay = ClassBinaryOverlay.ClassFactory(stream, package);

        overlay.EditorID.ShouldBe("TestRecord");
        overlay.TagSkills.ToArray().ShouldBe(new ActorValue[4]);
        overlay.Teaches.ShouldBeNull();
        overlay.Attributes.ShouldBeEmpty();
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }

    [Fact]
    public void Book_OverlayReadsDataFieldsAsDefaults()
    {
        var bytes = MakeRecordBytes("BOOK");
        var direct = Book.CreateFromBinary(DirectFrame(bytes));
        var (stream, package) = OverlayArgs(bytes);
        var overlay = BookBinaryOverlay.BookFactory(stream, package);

        overlay.EditorID.ShouldBe("TestRecord");
        overlay.Skill.ShouldBeNull();
        overlay.Value.ShouldBe(0);
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }
}

using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;
using Skyrim = Mutagen.Bethesda.Skyrim;
using Starfield = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// A record of an unexpected type inside a group is skipped by its 4-byte major record length
/// </summary>
public class GroupForeignRecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static MutagenFrame Frame(GameConstants constants, byte[] bytes)
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new MutagenFrame(new MutagenMemoryReadStream(bytes, new ParsingMeta(constants, TestModKey, masters)));
    }

    private static byte[] Subrecord(string type, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] MajorRecord(string type, uint formId, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(content.Length));
        stream.Write(BitConverter.GetBytes(0));
        stream.Write(BitConverter.GetBytes(formId));
        stream.Write(BitConverter.GetBytes(0));
        stream.Write(BitConverter.GetBytes((ushort)44));
        stream.Write(BitConverter.GetBytes((ushort)0));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] Group(int label, int groupType, params byte[][] contents)
    {
        var body = contents.SelectMany(x => x).ToArray();
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("GRUP"));
        stream.Write(BitConverter.GetBytes(24 + body.Length));
        stream.Write(BitConverter.GetBytes(label));
        stream.Write(BitConverter.GetBytes(groupType));
        stream.Write(BitConverter.GetBytes(0));
        stream.Write(BitConverter.GetBytes(0));
        stream.Write(body);
        return stream.ToArray();
    }

    private static byte[] Keyword(uint formId, string editorId)
    {
        return MajorRecord("KYWD", formId, Subrecord("EDID", Encoding.ASCII.GetBytes(editorId + "\0")));
    }

    private static byte[] KeywordGroupWithForeignRecord(int foreignContentLength)
    {
        return Group(
            new RecordType("KYWD").TypeInt,
            groupType: 0,
            Keyword(0x800, "Keyword1"),
            MajorRecord("GLOB", 0x801, new byte[foreignContentLength]),
            Keyword(0x802, "Keyword2"));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(ushort.MaxValue)]
    [InlineData(ushort.MaxValue + 3)]
    [InlineData(0x2001C)]
    public void StarfieldGroup_ForeignRecord_FollowingRecordsKept(int foreignContentLength)
    {
        var bytes = KeywordGroupWithForeignRecord(foreignContentLength);
        var group = Starfield.StarfieldGroup<Starfield.Keyword>.CreateFromBinary(Frame(GameConstants.Starfield, bytes));
        group.Records.Select(x => x.EditorID).ShouldBe(new[] { "Keyword1", "Keyword2" });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(ushort.MaxValue)]
    [InlineData(ushort.MaxValue + 3)]
    [InlineData(0x2001C)]
    public void SkyrimGroup_ForeignRecord_FollowingRecordsKept(int foreignContentLength)
    {
        var bytes = KeywordGroupWithForeignRecord(foreignContentLength);
        var group = Skyrim.SkyrimGroup<Skyrim.Keyword>.CreateFromBinary(Frame(GameConstants.SkyrimSE, bytes));
        group.Records.Select(x => x.EditorID).ShouldBe(new[] { "Keyword1", "Keyword2" });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(ushort.MaxValue)]
    [InlineData(ushort.MaxValue + 3)]
    [InlineData(0x2001C)]
    public void StarfieldListGroup_ForeignRecord_FollowingBlocksKept(int foreignContentLength)
    {
        var bytes = Group(
            new RecordType("CELL").TypeInt,
            groupType: 0,
            MajorRecord("GLOB", 0x801, new byte[foreignContentLength]),
            Group(label: 3, groupType: (int)Starfield.GroupTypeEnum.InteriorCellBlock));
        var group = Starfield.StarfieldListGroup<Starfield.CellBlock>.CreateFromBinary(Frame(GameConstants.Starfield, bytes));
        group.Records.Select(x => x.BlockNumber).ShouldBe(new[] { 3 });
    }
}

using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary.Streams;

// C66: StreamHeaderMixIn's Get* methods look without moving the stream and its Read* methods move past what they read,
// as their docs say, with one documented exception: GetSubrecord(stream, RecordType) moves past the subrecord (Starfield's
// AComponent readers and GameplayOption rely on it). Fixed here: TryGetModHeader moved (so TryGetModHeaderFrame and
// TryReadModHeaderFrame built their frame from the bytes after the header) and ReadVariableHeader didn't move for a
// subrecord.
public class StreamHeaderPositionTests
{
    private static readonly GameConstants Constants = GameConstants.SkyrimSE;

    private static ParsingMeta Meta() => new(Constants, ModKey.FromNameAndExtension("Test.esp"), SeparatedMasterPackage.EmptyNull);

    private static byte[] Sub(string type, params byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    // TES4 with one HEDR: a 24-byte header and 18 bytes of content
    private static readonly byte[] Tes4 = Encoding.ASCII.GetBytes("TES4")
        .Concat(BitConverter.GetBytes(18)).Concat(new byte[16])
        .Concat(Sub("HEDR", new byte[12])).ToArray();

    private static readonly byte[] Subs = Sub("EDID", 1, 2, 0).Concat(Sub("DATA", 7, 7, 7, 7)).ToArray();

    private static MutagenMemoryReadStream Stream(byte[] bytes) => new(bytes, Meta());

    [Fact]
    public void ModHeader_GetLooks_ReadMoves()
    {
        var s = Stream(Tes4);
        s.TryGetModHeader(out var header).ShouldBeTrue();
        s.Position.ShouldBe(0);
        header.RecordType.ShouldBe(new RecordType("TES4"));
        s.GetModHeader().TotalLength.ShouldBe(Tes4.Length);
        s.Position.ShouldBe(0);

        // the frame covers the whole TES4, from its signature (it began 24 bytes later while TryGetModHeader moved)
        s.TryGetModHeaderFrame(out var frame).ShouldBeTrue();
        s.Position.ShouldBe(0);
        frame.HeaderAndContentData.ToArray().ShouldBe(Tes4);
        frame.Content.ToArray().ShouldBe(Tes4[24..]);

        s.TryReadModHeaderFrame(out var read).ShouldBeTrue();
        s.Position.ShouldBe(Tes4.Length);
        read.HeaderAndContentData.ToArray().ShouldBe(Tes4);

        var r = Stream(Tes4);
        r.TryReadModHeader(out _).ShouldBeTrue();
        r.Position.ShouldBe(Constants.ModHeaderLength);
    }

    [Fact]
    public void Subrecord_GetLooks_ReadMoves_GetWithATypeMoves()
    {
        var s = Stream(Subs);
        s.GetSubrecordHeader().RecordType.ShouldBe(new RecordType("EDID"));
        s.GetSubrecord().RecordType.ShouldBe(new RecordType("EDID"));
        s.TryGetSubrecord(new RecordType("EDID"), out _).ShouldBeTrue();
        s.Position.ShouldBe(0);

        // the documented exception: GetSubrecord with a type moves past the subrecord, like ReadSubrecord
        var edid = s.GetSubrecord(new RecordType("EDID"));
        edid.Content.ToArray().ShouldBe(new byte[] { 1, 2, 0 });
        s.Position.ShouldBe(9);
        // a subrecord of another type throws and leaves the position
        Should.Throw<ArgumentException>(() => s.GetSubrecord(new RecordType("EDID")));
        s.Position.ShouldBe(9);

        s.ReadSubrecord(new RecordType("DATA")).Content.ToArray().ShouldBe(new byte[] { 7, 7, 7, 7 });
        s.Position.ShouldBe(Subs.Length);
    }

    [Fact]
    public void VariableHeader_GetLooks_ReadMoves()
    {
        var s = Stream(Subs);
        s.GetVariableHeader(subRecords: true).RecordType.ShouldBe(new RecordType("EDID"));
        s.Position.ShouldBe(0);
        // a subrecord's header is read past, as a group's or record's is
        var header = s.ReadVariableHeader(subRecords: true);
        header.RecordType.ShouldBe(new RecordType("EDID"));
        s.Position.ShouldBe(Constants.SubConstants.HeaderLength);

        var r = Stream(Tes4);
        r.ReadVariableHeader(subRecords: false).RecordType.ShouldBe(new RecordType("TES4"));
        r.Position.ShouldBe(Constants.MajorConstants.HeaderLength);
    }
}

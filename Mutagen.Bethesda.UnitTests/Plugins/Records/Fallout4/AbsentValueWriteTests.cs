using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// Values that the readers leave absent (null) must be writable, so that a record
// read by Mutagen, or a freshly constructed one, can be written back.
public class AbsentValueWriteTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);
    private static readonly FormKey TestFormKey = new(TestModKey, 0x800);

    private static ParsingMeta Meta() => new(
        GameConstants.Fallout4,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] Record(string type, params byte[][] subrecords)
    {
        var content = subrecords.SelectMany(x => x).ToArray();
        var header = new byte[GameConstants.Fallout4.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), TestFormKey.ID);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 131);
        return header.Concat(content).ToArray();
    }

    private const uint DeletedFlag = 0x20;

    private static byte[] Deleted(byte[] record)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), DeletedFlag);
        return record;
    }

    private static byte[] EditorId(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static MutagenFrame Frame(byte[] bytes) => new(new MutagenMemoryReadStream(bytes, Meta()));

    private static byte[] Write(Action<MutagenWriter> write)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Fallout4)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }))
        {
            write(writer);
        }
        return stream.ToArray();
    }

    private static IEnumerable<ISoundDescriptorGetter> ReadSoundDescriptor(byte[] bytes)
    {
        yield return SoundDescriptor.CreateFromBinary(Frame(bytes));
        var meta = Meta();
        yield return SoundDescriptorBinaryOverlay.SoundDescriptorFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void SoundDescriptor_NoCnam_WritesBackWithoutCnam()
    {
        var bytes = Record("SNDR", EditorId("TestSndr"));
        foreach (var sndr in ReadSoundDescriptor(bytes))
        {
            sndr.Data.ShouldBeNull();
            Write(w => sndr.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void SoundDescriptor_New_WritesAndReadsBackNullData()
    {
        var sndr = new SoundDescriptor(TestFormKey, Fallout4Release.Fallout4)
        {
            EditorID = "TestSndr",
        };
        var written = Write(w => sndr.WriteToBinary(w));
        foreach (var reread in ReadSoundDescriptor(written))
        {
            reread.EditorID.ShouldBe("TestSndr");
            reread.Data.ShouldBeNull();
        }
    }

    [Fact]
    public void ObjectModification_NoData_OverlayReadsAndWritesLikeDirectImport()
    {
        var bytes = Record("OMOD", EditorId("TestOmod"));
        var direct = AObjectModification.CreateFromBinary(Frame(bytes), default);
        var meta = Meta();
        var overlay = AObjectModificationBinaryOverlay.AObjectModificationFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta),
            default);

        overlay.ShouldBeAssignableTo<IUnknownObjectModificationGetter>();
        overlay.Unknown.ShouldBe(direct.Unknown);
        overlay.MaxRank.ShouldBe(direct.MaxRank);
        overlay.LevelTierScaledOffset.ShouldBe(direct.LevelTierScaledOffset);
        overlay.AttachPoint.IsNull.ShouldBeTrue();
        Write(w => overlay.WriteToBinary(w)).ShouldBe(Write(w => direct.WriteToBinary(w)));
    }

    [Fact]
    public void ObjectModification_DeletedNoData_OverlayReadsAndWritesLikeDirectImport()
    {
        var bytes = Deleted(Record("OMOD", EditorId("TestOmod")));
        var direct = AObjectModification.CreateFromBinary(Frame(bytes), default);
        var meta = Meta();
        var overlay = AObjectModificationBinaryOverlay.AObjectModificationFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta),
            default);

        // Deleted records are written as their header only
        var written = Write(w => direct.WriteToBinary(w));
        written.ShouldBe(Deleted(Record("OMOD")));
        Write(w => overlay.WriteToBinary(w)).ShouldBe(written);
        foreach (var omod in new[] { direct, overlay })
        {
            omod.IsDeleted.ShouldBeTrue();
            omod.ShouldBeAssignableTo<IUnknownObjectModificationGetter>()!
                .ModificationType.ShouldBe(RecordType.Null);
        }
    }
}

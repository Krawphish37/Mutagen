using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield stores sound levels as None 0, Silent 1, Quiet 2, Normal 3, Loud 4, Very Loud 5.
public class SoundLevelTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static byte[] Subrecord(string type, params byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] Record(string type, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] Write(Action<MutagenWriter> write)
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
            write(writer);
        }
        return memStream.ToArray();
    }

    private static IEnumerable<IDoorGetter> ReadDoor(byte[] bytes)
    {
        yield return Door.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return DoorBinaryOverlay.DoorFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Theory]
    [InlineData(0, SoundLevel.None)]
    [InlineData(1, SoundLevel.Silent)]
    [InlineData(2, SoundLevel.Quiet)]
    [InlineData(3, SoundLevel.Normal)]
    [InlineData(4, SoundLevel.Loud)]
    [InlineData(5, SoundLevel.VeryLoud)]
    public void Door_SoundLevel_ReadsStarfieldValues(byte stored, SoundLevel expected)
    {
        var bytes = Record("DOOR", Subrecord("DEVT", stored, 0, 0, 0));
        foreach (var door in ReadDoor(bytes))
        {
            door.SoundLevel.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(SoundLevel.None, 0)]
    [InlineData(SoundLevel.Loud, 4)]
    [InlineData(SoundLevel.VeryLoud, 5)]
    public void Door_SoundLevel_WritesStarfieldValues(SoundLevel level, byte expected)
    {
        var door = new Door(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            SoundLevel = level,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => door.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("DEVT")).Content.ToArray().ShouldBe(new byte[] { expected, 0, 0, 0 });
    }
}

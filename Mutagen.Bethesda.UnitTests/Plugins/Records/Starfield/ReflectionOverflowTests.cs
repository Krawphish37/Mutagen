using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using Activator = Mutagen.Bethesda.Starfield.Activator;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// REFL subrecords larger than 64 KB are stored as XXXX (uint32 length) followed by a REFL with a zero length header
/// </summary>
public class ReflectionOverflowTests
{
    private const int OverflowLength = ushort.MaxValue + 4465;
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static byte[] Payload(int length)
    {
        var ret = new byte[length];
        for (int i = 0; i < length; i++)
        {
            ret[i] = (byte)(i * 7);
        }
        return ret;
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static void WriteOverflowSubrecord(MemoryStream stream, string type, byte[] content)
    {
        WriteSubrecord(stream, "XXXX", BitConverter.GetBytes(content.Length));
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes((ushort)0));
        stream.Write(content);
    }

    private static byte[] MakeRecord(string recordType, Action<MemoryStream> writeBody)
    {
        var content = new MemoryStream();
        writeBody(content);
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(recordType));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] Write(IStarfieldMajorRecordGetter record)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, GameConstants.Starfield, dispose: false))
        {
            writer.MetaData.MasterReferences = new MasterReferenceCollection(TestModKey, []);
            writer.MetaData.SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(writer.MetaData.MasterReferences);
            ((IBinaryItem)record).WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static List<(string Type, ushort Length)> RawSubrecordHeaders(byte[] record)
    {
        var ret = new List<(string, ushort)>();
        var pos = 24;
        int? overflow = null;
        while (pos < record.Length)
        {
            var type = Encoding.ASCII.GetString(record, pos, 4);
            var len = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(pos + 4));
            ret.Add((type, len));
            var contentLength = overflow ?? len;
            overflow = type == "XXXX" ? BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(pos + 6)) : null;
            pos += 6 + contentLength;
        }
        return ret;
    }

    private static IEnumerable<IEffectSequenceGetter> ReadEffectSequence(byte[] bytes)
    {
        yield return EffectSequence.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return EffectSequenceBinaryOverlay.EffectSequenceFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IAtmosphereGetter> ReadAtmosphere(byte[] bytes)
    {
        yield return Atmosphere.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return AtmosphereBinaryOverlay.AtmosphereFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IActivatorGetter> ReadActivator(byte[] bytes)
    {
        yield return Activator.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return ActivatorBinaryOverlay.ActivatorFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void EffectSequenceOverflowReflRead()
    {
        var payload = Payload(OverflowLength);
        var bytes = MakeRecord("EFSQ", s =>
        {
            WriteSubrecord(s, "EDID", Encoding.ASCII.GetBytes("TestEffectSequence\0"));
            WriteOverflowSubrecord(s, "REFL", payload);
        });

        foreach (var effectSequence in ReadEffectSequence(bytes))
        {
            effectSequence.EditorID.ShouldBe("TestEffectSequence");
            effectSequence.REFL.ShouldNotBeNull();
            effectSequence.REFL!.Value.ToArray().ShouldBe(payload);
        }
    }

    [Fact]
    public void AtmosphereOverflowReflReadKeepsFollowingSubrecords()
    {
        var payload = Payload(OverflowLength);
        var bytes = MakeRecord("ATMO", s =>
        {
            WriteSubrecord(s, "EDID", Encoding.ASCII.GetBytes("TestAtmosphere\0"));
            WriteOverflowSubrecord(s, "REFL", payload);
            WriteSubrecord(s, "RFDP", BitConverter.GetBytes(0x801));
            WriteSubrecord(s, "RDIF", new byte[] { 1, 2, 3 });
        });

        foreach (var atmosphere in ReadAtmosphere(bytes))
        {
            atmosphere.EditorID.ShouldBe("TestAtmosphere");
            atmosphere.REFL.ShouldNotBeNull();
            atmosphere.REFL!.Value.ToArray().ShouldBe(payload);
            atmosphere.ReflectionParent.FormKey.ShouldBe(new FormKey(TestModKey, 0x801));
            atmosphere.ReflectionDiff!.Value.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
        }
    }

    [Fact]
    public void EffectSequenceOverflowReflWrite()
    {
        var payload = Payload(OverflowLength);
        var effectSequence = new EffectSequence(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestEffectSequence",
            REFL = payload
        };

        var bytes = Write(effectSequence);

        RawSubrecordHeaders(bytes).ShouldBe(new[]
        {
            ("EDID", (ushort)"TestEffectSequence\0".Length),
            ("XXXX", (ushort)4),
            ("REFL", (ushort)0),
        });
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24 + 6 + 19 + 6)).ShouldBe((uint)OverflowLength);

        foreach (var reread in ReadEffectSequence(bytes))
        {
            reread.REFL.ShouldNotBeNull();
            reread.REFL!.Value.ToArray().ShouldBe(payload);
        }
    }

    [Fact]
    public void ComponentOverflowReflRoundTrip()
    {
        var payload = Payload(OverflowLength);
        var activator = new Activator(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestActivator",
        };
        activator.Components.Add(new EffectSequenceComponent()
        {
            REFL = payload
        });
        activator.Components.Add(new LodOwnerComponent()
        {
            REFL = new byte[] { 4, 5, 6 }
        });

        var bytes = Write(activator);

        foreach (var reread in ReadActivator(bytes))
        {
            reread.Components.Count.ShouldBe(2);
            var effectSequence = reread.Components[0].ShouldBeAssignableTo<IEffectSequenceComponentGetter>()!;
            effectSequence.REFL!.Value.ToArray().ShouldBe(payload);
            var lodOwner = reread.Components[1].ShouldBeAssignableTo<ILodOwnerComponentGetter>()!;
            lodOwner.REFL!.Value.ToArray().ShouldBe(new byte[] { 4, 5, 6 });
        }
    }
}

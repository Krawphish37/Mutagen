using System.Buffers.Binary;
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

// REFR XLLD is a 4-byte light layer mask (same values as LIGH LLLD, Light.LightLayer),
// not a bool.  It used to be modelled as a Bool, so 0x0F (all layers) was read as true
// and written back as 0x01 (Default only).
public class PlacedObjectLightLayerDataTests
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

    private static byte[] MakePlacedObjectBytes(uint? xlld)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "NAME", BitConverter.GetBytes(0x801u));
        if (xlld.HasValue)
        {
            WriteSubrecord(content, "XLLD", BitConverter.GetBytes(xlld.Value));
        }
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
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, GameConstants.Starfield, dispose: false))
        {
            writer.MetaData.MasterReferences = new MasterReferenceCollection(TestModKey);
            writer.MetaData.SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(writer.MetaData.MasterReferences);
            placed.WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static uint? WrittenXlld(byte[] bytes)
    {
        var stream = new MutagenMemoryReadStream(bytes, Meta());
        var rec = stream.ReadMajorRecord();
        if (!rec.TryFindSubrecord(RecordTypes.XLLD, out var xlld)) return null;
        xlld.ContentLength.ShouldBe(4);
        return BinaryPrimitives.ReadUInt32LittleEndian(xlld.Content);
    }

    [Theory]
    [InlineData(0x01u)]
    [InlineData(0x0Du)]
    [InlineData(0x0Fu)]
    public void ReadsLayerMask(uint value)
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(value)))
        {
            placed.LightLayerData.ShouldBe((Light.LightLayer)value);
        }
    }

    [Fact]
    public void Absent_IsNull()
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(null)))
        {
            placed.LightLayerData.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(0x01u)]
    [InlineData(0x0Du)]
    [InlineData(0x0Fu)]
    public void RoundTripKeepsLayerMask(uint value)
    {
        var bytes = MakePlacedObjectBytes(value);
        foreach (var placed in ReadBoth(bytes))
        {
            WrittenXlld(Write(placed)).ShouldBe(value);
        }
    }

    [Fact]
    public void RoundTripAbsent_WritesNoXlld()
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(null)))
        {
            WrittenXlld(Write(placed)).ShouldBeNull();
        }
    }
}

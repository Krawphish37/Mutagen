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

public class LightLayerTests
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

    private static byte[] MakeLightBytes(uint layer)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestLight\0"));
        WriteSubrecord(content, "LLLD", BitConverter.GetBytes(layer));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("LIGH"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<ILightGetter> ReadBoth(byte[] bytes)
    {
        yield return Light.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return LightBinaryOverlay.LightFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void LightLayer_IsFlagsEnum()
    {
        typeof(Light.LightLayer).IsDefined(typeof(FlagsAttribute), inherit: false).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0x0Fu, "Default, Helmet, ShipInterior, ShipExterior")]
    [InlineData(0x0Du, "Default, ShipInterior, ShipExterior")]
    [InlineData(0x01u, "Default")]
    public void CombinedLayers_ReadAndPrintAsMemberNames(uint raw, string expected)
    {
        foreach (var light in ReadBoth(MakeLightBytes(raw)))
        {
            light.EditorID.ShouldBe("TestLight");
            ((uint)light.Layer).ShouldBe(raw);
            light.Layer.ToString().ShouldBe(expected);
            light.Print().ShouldContain($"Layer => {expected}");
        }
    }

    [Fact]
    public void AllFourLayers_HasEveryLayerFlag()
    {
        var all = Light.LightLayer.Default
            | Light.LightLayer.Helmet
            | Light.LightLayer.ShipInterior
            | Light.LightLayer.ShipExterior;
        foreach (var light in ReadBoth(MakeLightBytes(0x0F)))
        {
            light.Layer.ShouldBe(all);
            light.Layer.HasFlag(Light.LightLayer.ShipInterior).ShouldBeTrue();
        }
    }
}

using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Fallout4;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// SPGD DATA: '# of Subtextures X/Y' are 32-bit integers (xEdit; Skyrim's Mutagen definition has them as UInt32, and
// Starfield's SPGD stores 4 = 04 00 00 00). Fallout 4's definition read them as floats, so a count of 4 came out as
// 5.6E-45. One record is synthetic: the 96-byte DATA layout,
// with counts 4 and 2, plus the real RealRain (126FF2:Fallout4.esm). Both are read with both readers and written back
// byte for byte.
public class ShaderParticleGeometrySubtextureTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Fallout4, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] Data()
    {
        var data = new byte[96];
        BitConverter.GetBytes(1.2f).CopyTo(data, 8);   // RotationVelocity
        BitConverter.GetBytes(4u).CopyTo(data, 56);    // NumSubtexturesX
        BitConverter.GetBytes(2u).CopyTo(data, 64);    // NumSubtexturesY
        BitConverter.GetBytes(1u).CopyTo(data, 72);    // Type
        BitConverter.GetBytes(4000u).CopyTo(data, 80); // BoxSize
        BitConverter.GetBytes(0.22f).CopyTo(data, 88); // ParticleDensity
        return data;
    }

    private static byte[] Content()
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestParticles\0"));
        WriteSubrecord(content, "DATA", Data());
        WriteSubrecord(content, "MNAM", Encoding.ASCII.GetBytes("Effects\\Dust.dds\0"));
        return content.ToArray();
    }

    private static byte[] Record()
    {
        var body = Content();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("SPGD"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)131));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<IShaderParticleGeometryGetter> ReadBoth()
    {
        yield return ShaderParticleGeometry.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(), Meta())));
        var meta = Meta();
        yield return ShaderParticleGeometryBinaryOverlay.ShaderParticleGeometryFactory(
            new OverlayStream(Record(), meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void SubtextureCounts_AreIntegers()
    {
        foreach (var spgd in ReadBoth())
        {
            spgd.NumSubtexturesX.ShouldBe(4u);
            spgd.NumSubtexturesY.ShouldBe(2u);
            spgd.BoxSize.ShouldBe(4000u);
            spgd.ParticleDensity.ShouldBe(0.22f);

            var masters = new MasterReferenceCollection(TestModKey);
            var bundle = new WritingBundle(GameConstants.Fallout4)
            {
                MasterReferences = masters,
                SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
            };
            var stream = new MemoryStream();
            using (var writer = new MutagenWriter(stream, bundle, dispose: false))
            {
                spgd.WriteToBinary(writer);
            }
            GameConstants.Fallout4.MajorRecord(stream.ToArray()).Content.ToArray().ShouldBe(Content());
        }
    }

    // RealRain (126FF2:Fallout4.esm), whole record: # of Subtextures X/Y = 8 and 2 (all 3 SPGD in Fallout4.esm
    // store 8 and 2). The unused slots hold FF7FFF7F / FFFFFFFF, written back bit for bit.
    private const string RealRain = "4544494409005265616C5261696E0044415441600000C041440000000000000000FF7FFF7F0000C03F000000000000003F00000000000000000000000000000000FFFFFFFF0000000000000000080000000000000002000000000000000000000000000000B0040000FFFFFF7F0000E040000000004D4E414D2000656666656374735C4658576561746865725261696E64726F70732E4247454D00";

    [Fact]
    public void RealRain_SubtextureCounts()
    {
        var bytes = Convert.FromHexString(RealRain);
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("SPGD"));
        record.Write(BitConverter.GetBytes(bytes.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)131));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(bytes);
        var meta = Meta();
        var all = new IShaderParticleGeometryGetter[]
        {
            ShaderParticleGeometry.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(record.ToArray(), Meta()))),
            ShaderParticleGeometryBinaryOverlay.ShaderParticleGeometryFactory(new OverlayStream(record.ToArray(), meta), new BinaryOverlayFactoryPackage(meta)),
        };
        foreach (var spgd in all)
        {
            spgd.EditorID.ShouldBe("RealRain");
            spgd.NumSubtexturesX.ShouldBe(8u);
            spgd.NumSubtexturesY.ShouldBe(2u);
            spgd.BoxSize.ShouldBe(1200u);
            spgd.ParticleTexture.ShouldBe(@"effects\FXWeatherRaindrops.BGEM");
            var masters = new MasterReferenceCollection(TestModKey);
            var bundle = new WritingBundle(GameConstants.Fallout4)
            {
                MasterReferences = masters,
                SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
            };
            var stream = new MemoryStream();
            using (var writer = new MutagenWriter(stream, bundle, dispose: false))
            {
                spgd.WriteToBinary(writer);
            }
            GameConstants.Fallout4.MajorRecord(stream.ToArray()).Content.ToArray().ShouldBe(bytes);
        }
    }
}

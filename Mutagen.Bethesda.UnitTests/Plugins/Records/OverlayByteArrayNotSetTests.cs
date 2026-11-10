using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using Shouldly;
using Xunit;
using F4 = Mutagen.Bethesda.Fallout4;
using SK = Mutagen.Bethesda.Skyrim;
using SF = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// A non-nullable fixed length byte array that is not present in the binary
/// (left out after a Break, or its subrecord is missing) should read as the
/// mutable class default: a zeroed array of the declared length.
/// The overlay should agree with the full import.
/// </summary>
public class OverlayByteArrayNotSetTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta(GameConstants constants)
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(constants, TestModKey, masters);
    }

    private static byte[] Subrecord(string type, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] MajorRecord(string type, ushort formVersion, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(formVersion));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static MutagenFrame Frame(byte[] bytes, GameConstants constants)
    {
        return new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta(constants)));
    }

    private static T Overlay<T>(
        byte[] bytes,
        GameConstants constants,
        Func<OverlayStream, BinaryOverlayFactoryPackage, T> factory)
    {
        var meta = Meta(constants);
        return factory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static void ShouldBeZeros(ReadOnlyMemorySlice<byte> bytes, int length)
    {
        bytes.ToArray().ShouldBe(new byte[length]);
    }

    // Struct field after a Break, length below the shared Zeros buffer
    [Fact]
    public void SkyrimEnableParentUnknownAfterBreak()
    {
        var bytes = Subrecord("XESP", BitConverter.GetBytes(0x801));

        var direct = SK.EnableParent.CreateFromBinary(Frame(bytes, GameConstants.SkyrimSE));
        var overlay = Overlay(bytes, GameConstants.SkyrimSE,
            (s, p) => SK.EnableParentBinaryOverlay.EnableParentFactory(s, p));

        overlay.Versioning.ShouldBe(SK.EnableParent.VersioningBreaks.Break0);
        ShouldBeZeros(direct.Unknown, 3);
        ShouldBeZeros(overlay.Unknown, 3);
        direct.Equals(overlay).ShouldBeTrue();
    }

    // Struct field after a Break, length equal to the shared Zeros buffer
    [Fact]
    public void Fallout4WaterVelocityUnknown3AfterBreak()
    {
        var bytes = Subrecord("XWCU", new byte[0x30]);

        var direct = F4.WaterVelocity.CreateFromBinary(Frame(bytes, GameConstants.Fallout4));
        var overlay = Overlay(bytes, GameConstants.Fallout4,
            (s, p) => F4.WaterVelocityBinaryOverlay.WaterVelocityFactory(s, p));

        overlay.Versioning.ShouldBe(F4.WaterVelocity.VersioningBreaks.Break0);
        ShouldBeZeros(direct.Unknown3, 16);
        ShouldBeZeros(overlay.Unknown3, 16);
        direct.Equals(overlay).ShouldBeTrue();
    }

    // Field of a Data subrecord after a Break
    [Fact]
    public void SkyrimStaticUnusedAfterBreak()
    {
        var bytes = MajorRecord("STAT", 44,
            Subrecord("EDID", Encoding.ASCII.GetBytes("TestStatic\0")),
            Subrecord("DNAM", new byte[8]));

        var direct = SK.Static.CreateFromBinary(Frame(bytes, GameConstants.SkyrimSE));
        var overlay = Overlay(bytes, GameConstants.SkyrimSE,
            (s, p) => SK.StaticBinaryOverlay.StaticFactory(s, p));

        overlay.DNAMDataTypeState.ShouldBe(SK.Static.DNAMDataType.Break0);
        ShouldBeZeros(direct.Unused, 3);
        ShouldBeZeros(overlay.Unused, 3);
        direct.Equals(overlay).ShouldBeTrue();
    }

    // Field of a Data subrecord after a Break, as on every MGEF of Starfield.esm
    [Fact]
    public void StarfieldMagicEffectUnknown2AfterBreak()
    {
        var bytes = MajorRecord("MGEF", 581,
            Subrecord("EDID", Encoding.ASCII.GetBytes("TestMagicEffect\0")),
            Subrecord("DATA", new byte[0x88]));

        var direct = SF.MagicEffect.CreateFromBinary(Frame(bytes, GameConstants.Starfield));
        var overlay = Overlay(bytes, GameConstants.Starfield,
            (s, p) => SF.MagicEffectBinaryOverlay.MagicEffectFactory(s, p));

        overlay.DATADataTypeState.ShouldBe(SF.MagicEffect.DATADataType.Break0);
        ShouldBeZeros(direct.Unknown2, 6);
        ShouldBeZeros(overlay.Unknown2, 6);
        direct.Equals(overlay).ShouldBeTrue();
    }

    // Field of a Data subrecord that is longer than the shared Zeros buffer
    [Fact]
    public void Fallout4RaceUnknown2DataMissing()
    {
        var bytes = MajorRecord("RACE", 131,
            Subrecord("EDID", Encoding.ASCII.GetBytes("TestRace\0")));

        var direct = F4.Race.CreateFromBinary(Frame(bytes, GameConstants.Fallout4));
        var overlay = Overlay(bytes, GameConstants.Fallout4,
            (s, p) => F4.RaceBinaryOverlay.RaceFactory(s, p));

        ShouldBeZeros(direct.Unknown2, 36);
        ShouldBeZeros(overlay.Unknown2, 36);
    }

    // Typed subrecord that is missing. RSGD DNAM was a fixed byte array; it is now the decoded
    // GenerationData struct, which must read as all zeros in both readers when DNAM is absent.
    [Fact]
    public void StarfieldResourceGenerationDataItemGenerationDataMissing()
    {
        var bytes = MajorRecord("RSGD", 581,
            Subrecord("EDID", Encoding.ASCII.GetBytes("TestResourceGenerationData\0")),
            Subrecord("RNAM", BitConverter.GetBytes(0x801)));

        var direct = SF.ResourceGenerationData.CreateFromBinary(Frame(bytes, GameConstants.Starfield));
        var overlay = Overlay(bytes, GameConstants.Starfield,
            (s, p) => SF.ResourceGenerationDataBinaryOverlay.ResourceGenerationDataFactory(s, p));

        direct.Items.Count.ShouldBe(1);
        overlay.Items.Count.ShouldBe(1);
        direct.Items[0].GenerationData.Biome.Common.VeinNodesMin.ShouldBe(0f);
        overlay.Items[0].GenerationData.Biome.Common.VeinNodesMin.ShouldBe(0f);
        overlay.Items[0].GenerationData.Cell.Everywhere.Blobbiness.ShouldBe(0f);
        direct.Equals(overlay).ShouldBeTrue();
    }
}

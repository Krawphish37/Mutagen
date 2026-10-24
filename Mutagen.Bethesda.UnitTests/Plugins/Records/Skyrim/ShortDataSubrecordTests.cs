using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public class ShortDataSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.SkyrimSE, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    // An ammunition whose DATA stops after Projectile and Flags, followed by its ONAM
    private static byte[] MakeAmmunitionBytes()
    {
        var data = new MemoryStream();
        data.Write(BitConverter.GetBytes(0x801u));
        data.Write(BitConverter.GetBytes(0x2u));

        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestAmmo\0"));
        WriteSubrecord(content, "DATA", data.ToArray());
        WriteSubrecord(content, "ONAM", Encoding.ASCII.GetBytes("Ammo\0"));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("AMMO"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)44));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    [Fact]
    public void Ammunition_ShortData_OverlayDoesNotReadNextSubrecord()
    {
        var bytes = MakeAmmunitionBytes();
        var direct = Ammunition.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        var overlay = AmmunitionBinaryOverlay.AmmunitionFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));

        overlay.Projectile.FormKey.ShouldBe(new FormKey(TestModKey, 0x801));
        overlay.Flags.ShouldBe((Ammunition.Flag)0x2);
        overlay.Damage.ShouldBe(0f);
        overlay.Value.ShouldBe(0u);
        overlay.Weight.ShouldBe(0f);
        overlay.ShortName.ShouldBe("Ammo");
        // The two parses mark the break before Weight differently on a DATA this short, so only the field values are compared
        overlay.DeepCopy().Equals(direct, new Ammunition.TranslationMask(defaultOn: true) { DATADataTypeState = false }).ShouldBeTrue();
    }

    // A class whose DATA stops before or inside SkillWeights, as the last subrecord, with the next record's bytes after it
    private static byte[] MakeClassBytes(int dataLength)
    {
        var data = new byte[dataLength];
        data[4] = 0xFF;
        data[5] = 7;
        for (int i = 6; i < dataLength; i++)
        {
            data[i] = 3;
        }

        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestClass\0"));
        WriteSubrecord(content, "DATA", data);
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("CLAS"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)44));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        record.Write(Enumerable.Repeat((byte)0x41, 64).ToArray());
        return record.ToArray();
    }

    [Theory]
    [InlineData(6)]
    [InlineData(16)]
    public void Class_ShortData_OverlayDictionaryReadsAsEmpty(int dataLength)
    {
        var bytes = MakeClassBytes(dataLength);
        var direct = Class.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        var overlay = ClassBinaryOverlay.ClassFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));

        overlay.MaxTrainingLevel.ShouldBe((byte)7);
        direct.SkillWeights.ShouldBeEmpty();
        overlay.SkillWeights.ShouldBeEmpty();
        overlay.BleedoutDefault.ShouldBe(0f);
        overlay.StatWeights.ShouldBeEmpty();
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }
}

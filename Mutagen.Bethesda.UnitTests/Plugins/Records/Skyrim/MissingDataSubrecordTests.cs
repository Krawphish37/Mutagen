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

public class MissingDataSubrecordTests
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

    // A record with only an EDID, so its DATA subrecord is missing
    private static byte[] MakeRecordBytes(string recordType)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestRecord\0"));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(recordType));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)44));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static MutagenFrame DirectFrame(byte[] bytes) => new(new MutagenMemoryReadStream(bytes, Meta()));

    private static (OverlayStream Stream, BinaryOverlayFactoryPackage Package) OverlayArgs(byte[] bytes)
    {
        var meta = Meta();
        return (new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void MagicEffect_OverlayReadsDataFieldsAsDefaults()
    {
        var bytes = MakeRecordBytes("MGEF");
        var direct = MagicEffect.CreateFromBinary(DirectFrame(bytes));
        var (stream, package) = OverlayArgs(bytes);
        var overlay = MagicEffectBinaryOverlay.MagicEffectFactory(stream, package);

        overlay.EditorID.ShouldBe("TestRecord");
        overlay.CastingLight.IsNull.ShouldBeTrue();
        overlay.HitShader.IsNull.ShouldBeTrue();
        overlay.Projectile.IsNull.ShouldBeTrue();
        overlay.PerkToApply.IsNull.ShouldBeTrue();
        overlay.Flags.ShouldBe(default(MagicEffect.Flag));
        overlay.BaseCost.ShouldBe(0f);
        overlay.CastType.ShouldBe(default(CastType));
        overlay.CastingSoundLevel.ShouldBe(default(SoundLevel));
        // Unset enums read as zero in the overlay rather than their xml default of None, which is separate from this
        var copy = overlay.DeepCopy();
        copy.MagicSkill = direct.MagicSkill;
        copy.ResistValue = direct.ResistValue;
        copy.SecondActorValue = direct.SecondActorValue;
        copy.Equals(direct).ShouldBeTrue();
    }

    [Fact]
    public void Class_OverlayReadsDataFieldsAsDefaults()
    {
        var bytes = MakeRecordBytes("CLAS");
        var direct = Class.CreateFromBinary(DirectFrame(bytes));
        var (stream, package) = OverlayArgs(bytes);
        var overlay = ClassBinaryOverlay.ClassFactory(stream, package);

        overlay.EditorID.ShouldBe("TestRecord");
        overlay.Teaches.ShouldBeNull();
        overlay.SkillWeights.ShouldBeEmpty();
        overlay.StatWeights.ShouldBeEmpty();
        overlay.MaxTrainingLevel.ShouldBe((byte)0);
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }

    [Fact]
    public void Race_OverlayReadsDataFieldsAsDefaults()
    {
        var bytes = MakeRecordBytes("RACE");
        var direct = Race.CreateFromBinary(DirectFrame(bytes));
        var (stream, package) = OverlayArgs(bytes);
        var overlay = RaceBinaryOverlay.RaceFactory(stream, package);

        overlay.EditorID.ShouldBe("TestRecord");
        overlay.Starting.ShouldBeEmpty();
        overlay.Regen.ShouldBeEmpty();
        overlay.BaseCarryWeight.ShouldBe(0f);
        // Race's Equals is not used here, as for this record it does not hold even between the direct read and its own DeepCopy
        overlay.Starting.ShouldBe(direct.Starting);
        overlay.Regen.ShouldBe(direct.Regen);
        overlay.BaseCarryWeight.ShouldBe(direct.BaseCarryWeight);
        overlay.Size.ShouldBe(direct.Size);
    }
}

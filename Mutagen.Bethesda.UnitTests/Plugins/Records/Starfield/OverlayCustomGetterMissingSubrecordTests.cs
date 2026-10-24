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

// Hand-written overlay getters for fields of a data subrecord must return the
// same defaults as the full import when that subrecord is absent.
public class OverlayCustomGetterMissingSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static byte[] Subrecord(string type, byte[] content)
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

    private static byte[] EditorID(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static byte[] Acbs(uint flags, ushort level)
    {
        var acbs = new byte[16];
        BitConverter.GetBytes(flags).CopyTo(acbs, 0);
        BitConverter.GetBytes((short)3).CopyTo(acbs, 4);
        BitConverter.GetBytes(level).CopyTo(acbs, 6);
        return Subrecord("ACBS", acbs);
    }

    private static IEnumerable<INpcGetter> ReadNpcs(byte[] bytes)
    {
        yield return Npc.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return NpcBinaryOverlay.NpcFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void Npc_WithoutAcbs_FlagsAndLevelAreDefault()
    {
        var bytes = Record("NPC_", EditorID("NpcEdidOnly"));

        foreach (var npc in ReadNpcs(bytes))
        {
            npc.Flags.ShouldBe(default(Npc.Flag));
            npc.Level.ShouldBeAssignableTo<INpcLevelGetter>()!.Level.ShouldBe((short)0);
            npc.XpValueOffset.ShouldBe((short)0);
            var copy = Should.NotThrow(() => npc.DeepCopy());
            copy.Flags.ShouldBe(default(Npc.Flag));
            copy.Level.ShouldBeOfType<NpcLevel>().Level.ShouldBe((short)0);
        }
    }

    [Fact]
    public void Npc_WithAcbs_ReadsNpcLevel()
    {
        var bytes = Record("NPC_", EditorID("NpcAcbs"), Acbs(0x2, 7));

        foreach (var npc in ReadNpcs(bytes))
        {
            ((uint)npc.Flags).ShouldBe(0x2u);
            npc.Level.ShouldBeAssignableTo<INpcLevelGetter>()!.Level.ShouldBe((short)7);
            npc.XpValueOffset.ShouldBe((short)3);
        }
    }

    [Fact]
    public void Npc_WithAcbs_ReadsPcLevelMult()
    {
        var bytes = Record("NPC_", EditorID("NpcAcbsMult"), Acbs(0x2 | 0x80, 1500));

        foreach (var npc in ReadNpcs(bytes))
        {
            ((uint)npc.Flags).ShouldBe(0x2u);
            npc.Level.ShouldBeAssignableTo<IPcLevelMultGetter>()!.LevelMult.ShouldBe(1.5f);
        }
    }
}

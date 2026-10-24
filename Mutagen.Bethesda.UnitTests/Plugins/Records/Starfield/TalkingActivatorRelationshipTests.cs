using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog.Testing.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class TalkingActivatorRelationshipTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private const uint TalkingActivatorId = 0xA00;
    private const uint RelationshipId = 0xA01;
    private const uint VoiceId = 0xB00;
    private const uint KeywordId = 0xB01;
    private const uint ParentId = 0xB02;
    private const uint ChildId = 0xB03;

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    private static byte[] MakeRecord(string type, uint flags, uint formId, byte[] body)
    {
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(flags));
        record.Write(BitConverter.GetBytes(formId));
        record.Write(BitConverter.GetBytes(0x34B6));
        record.Write(BitConverter.GetBytes((ushort)582));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] MakeGroup(string label, byte[] records)
    {
        var group = new MemoryStream();
        group.Write(Encoding.ASCII.GetBytes("GRUP"));
        group.Write(BitConverter.GetBytes(24 + records.Length));
        group.Write(Encoding.ASCII.GetBytes(label));
        group.Write(BitConverter.GetBytes(0));
        group.Write(BitConverter.GetBytes(0));
        group.Write(BitConverter.GetBytes(0));
        group.Write(records);
        return group.ToArray();
    }

    // Same subrecord layout as kinggathcreations_spaceship.esm TACT 020BB237
    private static byte[] TalkingActivatorRecord()
    {
        var body = new MemoryStream();
        WriteSubrecord(body, "EDID", Encoding.ASCII.GetBytes("TestTalkingActivator\0"));
        WriteSubrecord(body, "OBND", Concat(
            BitConverter.GetBytes((short)-1), BitConverter.GetBytes((short)-2), BitConverter.GetBytes((short)-3),
            BitConverter.GetBytes((short)1), BitConverter.GetBytes((short)2), BitConverter.GetBytes((short)3),
            BitConverter.GetBytes(0.5f), BitConverter.GetBytes(0.25f), BitConverter.GetBytes(1.25f)));
        WriteSubrecord(body, "ODTY", BitConverter.GetBytes(0f));
        WriteSubrecord(body, "FULL", Encoding.ASCII.GetBytes("Talker\0"));
        WriteSubrecord(body, "MODL", Encoding.ASCII.GetBytes("Test\\Talker.nif\0"));
        WriteSubrecord(body, "FLLD", BitConverter.GetBytes(1u));
        WriteSubrecord(body, "XFLG", new byte[] { 2 });
        WriteSubrecord(body, "KSIZ", BitConverter.GetBytes(1u));
        WriteSubrecord(body, "KWDA", BitConverter.GetBytes(KeywordId));
        WriteSubrecord(body, "PNAM", new byte[] { 0xCC, 0x4C, 0x33, 0x00 });
        WriteSubrecord(body, "FNAM", BitConverter.GetBytes((ushort)0));
        WriteSubrecord(body, "JNAM", BitConverter.GetBytes((ushort)360));
        WriteSubrecord(body, "VNAM", BitConverter.GetBytes(VoiceId));
        return MakeRecord("TACT", 0x0001_0000, TalkingActivatorId, body.ToArray());
    }

    // Same layout as sfta04.esm RELA FD016818
    private static byte[] RelationshipRecord()
    {
        var body = new MemoryStream();
        WriteSubrecord(body, "EDID", Encoding.ASCII.GetBytes("TestRelationship\0"));
        WriteSubrecord(body, "DATA", Concat(
            BitConverter.GetBytes(ParentId),
            BitConverter.GetBytes(ChildId),
            BitConverter.GetBytes((ushort)1),
            new byte[] { 0, 0 },
            BitConverter.GetBytes(0u)));
        return MakeRecord("RELA", 0, RelationshipId, body.ToArray());
    }

    /// <summary>
    /// Writes a plugin with Mutagen holding neighbouring groups, then appends hand-built TACT and RELA groups
    /// </summary>
    private static string MakePlugin(MockFileSystem fs)
    {
        var dir = $"{PathingUtil.DrivePrefix}SomeFolder";
        fs.Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, TestModKey.FileName);
        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        mod.Activators.AddNew("TestActivator");
        mod.Armors.AddNew("TestArmor");
        mod.EquipTypes.AddNew("TestEquipType");
        mod.Outfits.AddNew("TestOutfit");
        mod.WriteToBinary(path, new BinaryWriteParameters() { FileSystem = fs });
        fs.File.WriteAllBytes(path, Concat(
            fs.File.ReadAllBytes(path),
            MakeGroup("TACT", TalkingActivatorRecord()),
            MakeGroup("RELA", RelationshipRecord())));
        return path;
    }

    private static IStarfieldModGetter Read(MockFileSystem fs, string path, bool overlay)
    {
        var param = new BinaryReadParameters() { FileSystem = fs };
        return overlay
            ? StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param)
            : StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param);
    }

    private static List<(string Label, byte[] Records)> TopLevelGroups(byte[] bytes)
    {
        var ret = new List<(string, byte[])>();
        var pos = 24 + BitConverter.ToInt32(bytes, 4);
        while (pos < bytes.Length)
        {
            Encoding.ASCII.GetString(bytes, pos, 4).ShouldBe("GRUP");
            var size = BitConverter.ToInt32(bytes, pos + 4);
            ret.Add((Encoding.ASCII.GetString(bytes, pos + 8, 4), bytes[(pos + 24)..(pos + size)]));
            pos += size;
        }
        return ret;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TalkingActivatorAndRelationshipGroups_AreRead(bool overlay)
    {
        var fs = new MockFileSystem();
        var path = MakePlugin(fs);
        var mod = Read(fs, path, overlay);
        var records = mod.EnumerateMajorRecords().ToDictionary(x => x.FormKey, x => x.EditorID);
        records.ShouldContainKeyAndValue(new FormKey(TestModKey, TalkingActivatorId), "TestTalkingActivator");
        records.ShouldContainKeyAndValue(new FormKey(TestModKey, RelationshipId), "TestRelationship");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TalkingActivatorAndRelationshipGroups_SurviveWrite(bool overlay)
    {
        var fs = new MockFileSystem();
        var path = MakePlugin(fs);
        var outPath = Path.Combine(Path.GetDirectoryName(path)!, "Out.esp");
        var mod = Read(fs, path, overlay);
        mod.BeginWrite
            .ToPath(outPath)
            .WithNoLoadOrder()
            .WithFileSystem(fs)
            .NoModKeySync()
            .Write();

        var groups = TopLevelGroups(fs.File.ReadAllBytes(outPath));
        var labels = groups.Select(x => x.Label).ToList();
        labels.ShouldContain("TACT");
        labels.ShouldContain("RELA");
        // Same group order as the Creation Kit output
        labels.IndexOf("TACT").ShouldBe(labels.IndexOf("ACTI") + 1);
        labels.IndexOf("ARMO").ShouldBe(labels.IndexOf("TACT") + 1);
        labels.IndexOf("RELA").ShouldBe(labels.IndexOf("EQUP") + 1);
        labels.IndexOf("OTFT").ShouldBe(labels.IndexOf("RELA") + 1);
        // Every subrecord round trips byte for byte
        groups.Single(x => x.Label == "TACT").Records.ShouldBe(TalkingActivatorRecord());
        groups.Single(x => x.Label == "RELA").Records.ShouldBe(RelationshipRecord());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TalkingActivator_Fields(bool overlay)
    {
        var fs = new MockFileSystem();
        var mod = Read(fs, MakePlugin(fs), overlay);
        var tact = mod.TalkingActivators.ShouldHaveSingleItem();
        tact.FormKey.ShouldBe(new FormKey(TestModKey, TalkingActivatorId));
        tact.MajorRecordFlagsRaw.ShouldBe((int)TalkingActivator.MajorFlag.RandomAnimStart);
        tact.Name!.String.ShouldBe("Talker");
        tact.Model!.File.GivenPath.ShouldBe("Test\\Talker.nif");
        tact.Keywords!.Select(x => x.FormKey).ShouldBe(new[] { new FormKey(TestModKey, KeywordId) });
        tact.MarkerColor!.Value.R.ShouldBe((byte)0xCC);
        tact.MarkerColor!.Value.G.ShouldBe((byte)0x4C);
        tact.MarkerColor!.Value.B.ShouldBe((byte)0x33);
        tact.FNAM.ShouldBe((ushort)0);
        tact.ActivationAngle.ShouldBe((ushort)360);
        tact.Voice.FormKey.ShouldBe(new FormKey(TestModKey, VoiceId));
        ((IHasVoiceTypeGetter)tact).Voice.FormKey.ShouldBe(new FormKey(TestModKey, VoiceId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Relationship_Fields(bool overlay)
    {
        var fs = new MockFileSystem();
        var mod = Read(fs, MakePlugin(fs), overlay);
        var rela = mod.Relationships.ShouldHaveSingleItem();
        rela.FormKey.ShouldBe(new FormKey(TestModKey, RelationshipId));
        rela.Parent.FormKey.ShouldBe(new FormKey(TestModKey, ParentId));
        rela.Child.FormKey.ShouldBe(new FormKey(TestModKey, ChildId));
        rela.Rank.ShouldBe(Relationship.RankType.Ally);
        rela.Flags.ShouldBe((Relationship.Flag)0);
        rela.AssociationType.IsNull.ShouldBeTrue();
    }
}

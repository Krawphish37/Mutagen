using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
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

// A read and a save must keep what the file holds.
public class ZeroDropsTests
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

    private static byte[] Record(string type, uint flags, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(flags));
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

    private static IEnumerable<ITerminalMenuGetter> ReadTerminalMenu(byte[] bytes)
    {
        yield return TerminalMenu.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return TerminalMenuBinaryOverlay.TerminalMenuFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IGlobalGetter> ReadGlobal(byte[] bytes)
    {
        yield return Global.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return GlobalBinaryOverlay.GlobalFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IStaticGetter> ReadStatic(byte[] bytes)
    {
        yield return Static.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return StaticBinaryOverlay.StaticFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] Vmad(params byte[] fragmentData)
    {
        // Version 6, object format 2, no scripts, then the fragment data
        byte[] start = [6, 0, 2, 0, 0, 0];
        return start.Concat(fragmentData).ToArray();
    }

    private static byte[] SubrecordContent(byte[] record, RecordType type)
    {
        return GameConstants.Starfield.MajorRecord(record).FindSubrecord(type).Content.ToArray();
    }

    private static IEnumerable<IPlacedObjectGetter> ReadPlaced(byte[] bytes)
    {
        yield return PlacedObject.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return PlacedObjectBinaryOverlay.PlacedObjectFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static bool Has(byte[] record, RecordType type)
    {
        return GameConstants.Starfield.MajorRecord(record).EnumerateSubrecords().Any(x => x.RecordType == type);
    }

    [Fact]
    public void Reference_Position_ReadsTheSameBothWays()
    {
        var data = BitConverter.GetBytes(1f).Concat(BitConverter.GetBytes(2f)).Concat(BitConverter.GetBytes(3f))
            .Concat(BitConverter.GetBytes(4f)).Concat(BitConverter.GetBytes(5f)).Concat(BitConverter.GetBytes(6f)).ToArray();
        var bytes = Record("REFR", 0, Subrecord("NAME", 0, 8, 0, 0), Subrecord("DATA", data));
        foreach (var placed in ReadPlaced(bytes))
        {
            placed.Position.ShouldBe(new Noggog.P3Float(1, 2, 3));
            placed.Rotation.ShouldBe(new Noggog.P3Float(4, 5, 6));
            SubrecordContent(Write(w => placed.WriteToBinary(w)), RecordTypes.DATA).ShouldBe(data);
        }
    }

    [Fact]
    public void DeletedReference_KeepsItsBaseObject()
    {
        // As in Starfield.esm: a deleted REFR whose only content is its base object
        var bytes = Record("REFR", 0x20, Subrecord("NAME", 0x08, 0xF8, 0x03, 0x00));
        foreach (var placed in ReadPlaced(bytes))
        {
            placed.IsDeleted.ShouldBeTrue();
            placed.Base.FormKey.ID.ShouldBe(0x03F808u);
            Write(w => placed.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void DeletedReference_WithNoData_StaysWithout()
    {
        var bytes = Record("REFR", 0x20, Subrecord("NAME", 0, 8, 0, 0));
        foreach (var placed in ReadPlaced(bytes))
        {
            placed.HasPlacementData.ShouldBeFalse();
            Has(Write(w => placed.WriteToBinary(w)), RecordTypes.DATA).ShouldBeFalse();
        }
    }

    [Fact]
    public void DeletedReference_WithStoredZeros_KeepsThem()
    {
        var bytes = Record("REFR", 0x20, Subrecord("NAME", 0, 8, 0, 0), Subrecord("DATA", new byte[24]));
        foreach (var placed in ReadPlaced(bytes))
        {
            placed.HasPlacementData.ShouldBeTrue();
            Has(Write(w => placed.WriteToBinary(w)), RecordTypes.DATA).ShouldBeTrue();
        }
    }

    [Fact]
    public void DeletedNpcReference_WithNoData_StaysWithout()
    {
        var bytes = Record("ACHR", 0x20, Subrecord("NAME", 0, 8, 0, 0));
        var meta = Meta();
        IPlacedNpcGetter[] placed =
        [
            PlacedNpc.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()))),
            PlacedNpcBinaryOverlay.PlacedNpcFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta)),
        ];
        foreach (var item in placed)
        {
            item.HasPlacementData.ShouldBeFalse();
            Has(Write(w => item.WriteToBinary(w)), RecordTypes.DATA).ShouldBeFalse();
        }
    }

    [Fact]
    public void NewReference_StillGetsData()
    {
        var placed = new PlacedObject(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        Has(Write(w => placed.WriteToBinary(w)), RecordTypes.DATA).ShouldBeTrue();
    }

    [Fact]
    public void Npc_StopMarker_OnlyIfTheOriginalHadOne()
    {
        var without = Record("NPC_", 0, Subrecord("EDID", Encoding.ASCII.GetBytes("TestNpc\0")));
        var with = Record("NPC_", 0, Subrecord("EDID", Encoding.ASCII.GetBytes("TestNpc\0")), Subrecord("STOP"));
        var meta = Meta();
        var stop = new RecordType("STOP");
        Has(Write(w => Npc.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(without, Meta()))).WriteToBinary(w)), stop).ShouldBeFalse();
        Has(Write(w => Npc.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(with, Meta()))).WriteToBinary(w)), stop).ShouldBeTrue();
        Has(Write(w => NpcBinaryOverlay.NpcFactory(new OverlayStream(without, meta), new BinaryOverlayFactoryPackage(meta)).WriteToBinary(w)), stop).ShouldBeFalse();
        Has(Write(w => NpcBinaryOverlay.NpcFactory(new OverlayStream(with, meta), new BinaryOverlayFactoryPackage(meta)).WriteToBinary(w)), stop).ShouldBeTrue();
    }

    [Fact]
    public void Model_SecondMaterialSwapList_IsKept()
    {
        byte[] first = [1, 0, 0x01, 0x08, 0, 0];
        byte[] second = [1, 0, 0x02, 0x08, 0, 0];
        var bytes = Record(
            "STAT",
            0,
            Subrecord("OBND", new byte[24]),
            Subrecord("MODL", Encoding.ASCII.GetBytes("test.nif\0")),
            Subrecord("MOLM", first),
            Subrecord("MOLM", second));
        foreach (var stat in ReadStatic(bytes))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => stat.WriteToBinary(w)));
            var kept = written.EnumerateSubrecords().Where(x => x.RecordType == RecordTypes.MOLM).Select(x => x.Content.ToArray()).ToArray();
            kept.Length.ShouldBe(2);
            kept[0].ShouldBe(first);
            kept[1].ShouldBe(second);
        }
    }
}

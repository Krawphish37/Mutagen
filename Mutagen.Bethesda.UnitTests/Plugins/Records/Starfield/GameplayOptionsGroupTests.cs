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

public class GameplayOptionsGroupTests
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

    private static byte[] FormIds(params uint[] ids)
    {
        return ids.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private static byte[] MakeGameplayOptionsGroupBytes(byte bnam, params uint[] gogl)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestGameplayOptionsGroup\0"));
        WriteSubrecord(content, "BNAM", new[] { bnam });
        WriteSubrecord(content, "GOGL", FormIds(gogl));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("GPOG"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IGameplayOptionsGroupGetter ReadDirect(byte[] bytes)
    {
        return GameplayOptionsGroup.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IGameplayOptionsGroupGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return GameplayOptionsGroupBinaryOverlay.GameplayOptionsGroupFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IGameplayOptionsGroupGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static byte[] Write(IGameplayOptionsGroupGetter group)
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
            group.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static byte[] BnamContent(byte[] bytes)
    {
        return GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(RecordTypes.BNAM).Content.ToArray();
    }

    [Fact]
    public void Root_ReadsRootAndRoundTrips()
    {
        var bytes = MakeGameplayOptionsGroupBytes(1, 0x801, 0x802);

        foreach (var group in ReadBoth(bytes))
        {
            var root = group.Options.ShouldBeAssignableTo<IGameplayOptionsGroupRootGetter>();
            root!.GameplayOptionGroups.Select(x => x.FormKey)
                .ShouldBe(new[] { new FormKey(TestModKey, 0x801), new FormKey(TestModKey, 0x802) });
            var written = Write(group);
            BnamContent(written).ShouldBe(new byte[] { 1 });
            written.ShouldBe(bytes);
        }
    }

    [Fact]
    public void Leaf_ReadsLeafAndRoundTrips()
    {
        var bytes = MakeGameplayOptionsGroupBytes(0, 0x801);

        foreach (var group in ReadBoth(bytes))
        {
            var leaf = group.Options.ShouldBeAssignableTo<IGameplayOptionsGroupLeafGetter>();
            leaf!.GameplayOptions.Select(x => x.FormKey)
                .ShouldBe(new[] { new FormKey(TestModKey, 0x801) });
            var written = Write(group);
            BnamContent(written).ShouldBe(new byte[] { 0 });
            written.ShouldBe(bytes);
        }
    }

    [Fact]
    public void OverlayRoot_WrittenAndReread_StaysRoot()
    {
        var bytes = MakeGameplayOptionsGroupBytes(1, 0x801);

        var reread = ReadDirect(Write(ReadOverlay(bytes)));
        reread.Options.ShouldBeAssignableTo<IGameplayOptionsGroupRootGetter>();
    }
}

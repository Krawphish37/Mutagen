using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Fallout4.Internals;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// Quest ANAM is the next alias ID counter.  The CK does not reuse the IDs of deleted aliases,
// so it is often above the highest alias ID and must not be recomputed as max(alias ID) + 1.
public class QuestNextAliasIdTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.Fallout4,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] BuildQuestBytes(uint nextAliasId, params uint[] aliasIds)
    {
        var content = new List<byte>();
        content.AddRange(Subrecord("EDID", Encoding.ASCII.GetBytes("TestQuest\0")));
        content.AddRange(Subrecord("ANAM", BitConverter.GetBytes(nextAliasId)));
        foreach (var id in aliasIds)
        {
            content.AddRange(Subrecord("ALST", BitConverter.GetBytes(id)));
            content.AddRange(Subrecord("ALID", Encoding.ASCII.GetBytes($"Alias{id}\0")));
            content.AddRange(Subrecord("ALED", Array.Empty<byte>()));
        }
        var contentBytes = content.ToArray();

        var header = new byte[GameConstants.Fallout4.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes("QUST").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)contentBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x800);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 131);
        return header.Concat(contentBytes).ToArray();
    }

    private static IQuestGetter ReadDirect(byte[] bytes)
    {
        return Quest.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IQuestGetter ReadOverlay(byte[] bytes)
    {
        return QuestBinaryOverlay.QuestFactory(
            bytes,
            new BinaryOverlayFactoryPackage(Meta()));
    }

    private static IEnumerable<IQuestGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static byte[] Write(IQuestGetter item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Fallout4)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }))
        {
            QuestBinaryWriteTranslation.Instance.Write(writer, item, default);
        }
        return stream.ToArray();
    }

    private static uint? WrittenAnam(byte[] record)
    {
        var anam = RecordSpanExtensions.TryFindSubrecord(
            new ReadOnlyMemorySlice<byte>(record).Slice(GameConstants.Fallout4.MajorConstants.HeaderLength),
            GameConstants.Fallout4,
            RecordTypes.ANAM);
        return anam?.AsUInt32();
    }

    [Fact]
    public void Read_KeepsNextAliasId()
    {
        var bytes = BuildQuestBytes(8, 0, 3);
        foreach (var quest in ReadBoth(bytes))
        {
            quest.NextAliasID.ShouldBe(8u);
            quest.Aliases!.Count.ShouldBe(2);
        }
    }

    [Fact]
    public void Write_NextAliasIdAboveAliases_IsKept()
    {
        var bytes = BuildQuestBytes(8, 0, 3);
        foreach (var quest in ReadBoth(bytes))
        {
            var written = Write(quest);
            WrittenAnam(written).ShouldBe(8u);
            ReadDirect(written).NextAliasID.ShouldBe(8u);
        }
    }

    [Fact]
    public void Write_NextAliasIdBelowAliases_IsRaised()
    {
        var bytes = BuildQuestBytes(1, 0, 3);
        foreach (var quest in ReadBoth(bytes))
        {
            WrittenAnam(Write(quest)).ShouldBe(4u);
        }
    }

    [Fact]
    public void Write_NoNextAliasId_UsesMaxAliasIdPlusOne()
    {
        var quest = new Quest(FormKey.Factory("123456:Test.esp"), Fallout4Release.Fallout4)
        {
            Aliases = new ExtendedList<AQuestAlias>()
            {
                new QuestReferenceAlias() { ID = 0, Name = "Alias0" },
                new QuestReferenceAlias() { ID = 3, Name = "Alias3" },
            }
        };
        WrittenAnam(Write(quest)).ShouldBe(4u);
    }

    [Fact]
    public void Write_NextAliasIdSet_IsKept()
    {
        var quest = new Quest(FormKey.Factory("123456:Test.esp"), Fallout4Release.Fallout4)
        {
            NextAliasID = 10,
            Aliases = new ExtendedList<AQuestAlias>()
            {
                new QuestReferenceAlias() { ID = 0, Name = "Alias0" },
            }
        };
        WrittenAnam(Write(quest)).ShouldBe(10u);
    }
}

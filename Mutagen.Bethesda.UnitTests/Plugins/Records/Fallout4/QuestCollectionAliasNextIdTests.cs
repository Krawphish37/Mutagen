using Shouldly;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Fallout4.Internals;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// The ANAM (next alias ID) written for a quest must be above every alias ID,
// including the ALCS IDs of collection aliases.
public class QuestCollectionAliasNextIdTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esp");
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.Fallout4,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static Quest NewQuest(params AQuestAlias[] aliases)
    {
        return new Quest(new FormKey(TestModKey, 0x800), Fallout4Release.Fallout4)
        {
            EditorID = "TestQuest",
            Aliases = new ExtendedList<AQuestAlias>(aliases),
        };
    }

    private static QuestCollectionAlias Collection(params int[] aliasIds)
    {
        var ret = new QuestCollectionAlias();
        ret.Collection.SetTo(aliasIds.Select(x => new CollectionAlias() { AliasID = x }));
        return ret;
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

    private static uint WrittenAnam(byte[] record)
    {
        var frame = new MajorRecordFrame(GameConstants.Fallout4, record);
        return frame.FindSubrecord(RecordTypes.ANAM).AsUInt32();
    }

    private static IQuestGetter ParseDeep(byte[] bytes)
    {
        using var reader = new MutagenBinaryReadStream(new MemoryStream(bytes), Meta());
        return Quest.CreateFromBinary(new MutagenFrame(reader));
    }

    private static IQuestGetter ParseOverlay(byte[] bytes)
    {
        return QuestBinaryOverlay.QuestFactory(
            new OverlayStream(bytes, Meta()),
            new BinaryOverlayFactoryPackage(Meta()));
    }

    [Fact]
    public void CollectionAliasAboveReferenceAliases_CountedInAnam()
    {
        var quest = NewQuest(
            new QuestReferenceAlias() { ID = 0 },
            new QuestReferenceAlias() { ID = 1 },
            Collection(5));
        WrittenAnam(Write(quest)).ShouldBe(6u);
    }

    [Fact]
    public void OnlyCollectionAlias_CountedInAnam()
    {
        var quest = NewQuest(Collection(1, 3));
        WrittenAnam(Write(quest)).ShouldBe(4u);
    }

    [Fact]
    public void CollectionAliasBelowReferenceAliases_AnamFromReferenceAliases()
    {
        var quest = NewQuest(
            new QuestReferenceAlias() { ID = 0 },
            new QuestReferenceAlias() { ID = 2 },
            Collection(0));
        WrittenAnam(Write(quest)).ShouldBe(3u);
    }

    [Fact]
    public void EmptyCollectionAlias_AnamFromOtherAliases()
    {
        var quest = NewQuest(
            new QuestLocationAlias() { ID = 4 },
            Collection());
        WrittenAnam(Write(quest)).ShouldBe(5u);
    }

    [Fact]
    public void CollectionAlias_AnamSurvivesReadAndWrite()
    {
        var bytes = Write(NewQuest(
            new QuestReferenceAlias() { ID = 0 },
            Collection(7)));
        WrittenAnam(bytes).ShouldBe(8u);
        WrittenAnam(Write(ParseDeep(bytes))).ShouldBe(8u);
        WrittenAnam(Write(ParseOverlay(bytes))).ShouldBe(8u);
    }
}

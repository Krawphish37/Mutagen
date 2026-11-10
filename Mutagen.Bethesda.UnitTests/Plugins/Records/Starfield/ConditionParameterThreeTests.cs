using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C20: condition Parameter #3 (was Unknown3) is ParameterThree. What it holds depends on Run On (xEdit
// wbConditionParam3Decider), shown here by Starfield.esm's 96,486 conditions:
// - Linked Reference: a keyword (61 of 153; the rest 0), read as LinkedReferenceKeyword since 36ebd33c51;
// - Quest Alias: an alias (2,554; on QUST, 221 of 226 are one of the quest's own alias IDs);
// - Package Data: a package data input (65, all in PACK, 0 to 42);
// - Event Data: an event member, "R1" 0x3152 (325) or "R2" 0x3252 (114);
// - every other Run On: -1 (7 Subject and 1 Reference conditions hold stray small numbers).
// The typed accessors read the stored Int32; writes stay byte for byte.
public class ConditionParameterThreeTests
{
    // COM_Quest_Barrett_Q02 (002FAE:Starfield.esm): Run On Quest Alias, alias 137
    private const string QuestAliasCtda = "420000000000204101000000FEFFFFFF00000000050000000000000089000000";
    // ForceGreet (017BAB:Starfield.esm): Run On Package Data, input 4
    private const string PackageDataCtda = "0018FC4E0000803F4501F9440200000000000000060000000000000004000000";
    // OE_AustinF_EggThief (006E52:Starfield.esm): Run On Event Data, member R1
    private const string EventDataCtda = "000000000000803F94033B4110FF2B0000000000070000000000000052310000";

    private static IEnumerable<IConditionGetter> Read(string ctda) =>
        ReadBoth<IConditionRecordGetter>(Record("CNDF", Subrecord("CTDA", ctda)), f => ConditionRecord.CreateFromBinary(f), (s, p) => ConditionRecordBinaryOverlay.ConditionRecordFactory(s, p))
            .Select(r => r.Conditions.Single());

    private static void WritesBack(string ctda)
    {
        foreach (var rec in ReadBoth<IConditionRecordGetter>(Record("CNDF", Subrecord("CTDA", ctda)), f => ConditionRecord.CreateFromBinary(f), (s, p) => ConditionRecordBinaryOverlay.ConditionRecordFactory(s, p)))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => rec.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("CTDA")).Content.ToArray().ShouldBe(Hex(ctda));
        }
    }

    [Fact]
    public void QuestAlias()
    {
        foreach (var c in Read(QuestAliasCtda))
        {
            c.Data.ParameterThree.ShouldBe(137);
            c.Data.GetQuestAlias().ShouldBe(137);
            c.Data.GetPackageDataInput().ShouldBeNull();
            c.Data.GetEventDataMember().ShouldBeNull();
            c.Data.LinkedReferenceKeyword.IsNull.ShouldBeTrue();
        }
        WritesBack(QuestAliasCtda);
    }

    [Fact]
    public void PackageData()
    {
        foreach (var c in Read(PackageDataCtda))
        {
            c.Data.GetPackageDataInput().ShouldBe(4);
            c.Data.GetQuestAlias().ShouldBeNull();
        }
        WritesBack(PackageDataCtda);
    }

    [Fact]
    public void EventData()
    {
        foreach (var c in Read(EventDataCtda))
        {
            c.Data.ParameterThree.ShouldBe(0x3152);
            c.Data.GetEventDataMember().ShouldBe(GetEventDataConditionData.EventMember.Reference1);
            c.Data.GetQuestAlias().ShouldBeNull();
        }
        WritesBack(EventDataCtda);
    }

    [Fact]
    public void Setters()
    {
        var c = (Condition)new ConditionFloat { Data = new GetIsIDConditionData() };
        c.Data.SetEventDataMember(GetEventDataConditionData.EventMember.Location2);
        c.Data.RunOnType.ShouldBe(Condition.RunOnType.EventData);
        c.Data.ParameterThree.ShouldBe(0x324C);
        c.Data.SetQuestAlias(5);
        c.Data.GetQuestAlias().ShouldBe(5);
        c.Data.SetPackageDataInput(2);
        c.Data.GetPackageDataInput().ShouldBe(2);
    }
}

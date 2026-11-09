using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Condition Parameter #3 is a keyword FormID when Run On is Linked Reference (xEdit wbConditionParam3Decider: Linked
// Keyword [KYWD]); for Quest Alias it is an alias index. Mutagen kept it only as the Int32 Unknown3, so FormID remapping
// missed the keyword. In Starfield.esm 61 Linked Reference conditions have one (15 keywords). Now LinkedReferenceKeyword
// holds it, only for Run On Linked Reference; Unknown3 keeps the raw value.
public class ConditionLinkedKeywordTests
{
    // A CTDA of QUST 25B292: function 660, Run On Linked Reference, Parameter #3 LinkOutpostCargoLink (KYWD 2A6C3E)
    private const string LinkedRefCtda = "00-00-00-00-00-00-00-00-94-02-00-00-A0-F3-81-4A-B8-F3-81-4A-04-00-00-00-00-00-00-00-3E-6C-2A-00";
    // The same with Run On Quest Alias (5) and Parameter #3 = 3, an alias index: must stay a number
    private const string QuestAliasCtda = "00-00-00-00-00-00-00-00-94-02-00-00-A0-F3-81-4A-B8-F3-81-4A-05-00-00-00-00-00-00-00-03-00-00-00";

    private static IEnumerable<IConditionRecordGetter> Read(string ctda) =>
        ReadBoth<IConditionRecordGetter>(Record("CNDF", Subrecord("CTDA", ctda)), f => ConditionRecord.CreateFromBinary(f), (s, p) => ConditionRecordBinaryOverlay.ConditionRecordFactory(s, p));

    [Fact]
    public void LinkedReference_ReadsKeyword()
    {
        foreach (var rec in Read(LinkedRefCtda))
        {
            var data = rec.Conditions.Single().Data;
            data.RunOnType.ShouldBe(Condition.RunOnType.LinkedReference);
            data.LinkedReferenceKeyword.FormKey.ID.ShouldBe(0x2A6C3Eu);
            data.Unknown3.ShouldBe(0x2A6C3E);
            rec.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x2A6C3Eu);
        }
    }

    [Fact]
    public void QuestAlias_StaysANumber()
    {
        foreach (var rec in Read(QuestAliasCtda))
        {
            var data = rec.Conditions.Single().Data;
            data.LinkedReferenceKeyword.IsNull.ShouldBeTrue();
            data.Unknown3.ShouldBe(3);
            rec.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldNotContain(3u);
        }
    }

    [Theory]
    [InlineData(LinkedRefCtda)]
    [InlineData(QuestAliasCtda)]
    public void Ctda_WritesSameBytes(string ctda)
    {
        foreach (var rec in Read(ctda))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => rec.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("CTDA")).Content.ToArray().ShouldBe(Hex(ctda));
        }
    }

    [Fact]
    public void RemappedKeyword_IsWritten()
    {
        var rec = ConditionRecord.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
            new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenMemoryReadStream(Record("CNDF", Subrecord("CTDA", LinkedRefCtda)), Meta())));
        var from = rec.Conditions.Single().Data.LinkedReferenceKeyword.FormKey;
        var to = new FormKey(TestModKey, 0x999);
        rec.RemapLinks(new Dictionary<FormKey, FormKey> { [from] = to });
        var written = GameConstants.Starfield.MajorRecord(Write(w => rec.WriteToBinary(w)));
        (BitConverter.ToUInt32(written.FindSubrecord(new RecordType("CTDA")).Content.Slice(28, 4)) & 0xFFFFFF).ShouldBe(0x999u);
    }
}

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C68: a FormLinkOrIndex (a condition parameter that is an alias or package data index under UseAliases or
// UsePackageData, else a link) is one model on every path: Index is never left null (0 when nothing was read, as alias 0
// is a real value: 7 GetLineOfSight conditions in Starfield.esm use it), both readers keep the stored uint32 as the index
// and the FormKey it reads as, the write and Equals use the one the flags pick, and a copy keeps both.
public class FormLinkOrIndexModelTests
{
    private const byte UseAliases = 0x02;

    // GetLineOfSight (27): parameter 1 at CTDA offset 12, a FormLinkOrIndex
    private static byte[] Ctda(byte flags, uint parameter1)
    {
        var c = new byte[32];
        c[0] = flags;
        BitConverter.GetBytes(1f).CopyTo(c, 4);
        BitConverter.GetBytes((ushort)27).CopyTo(c, 8);
        BitConverter.GetBytes(parameter1).CopyTo(c, 12);
        BitConverter.GetBytes(-1).CopyTo(c, 28);
        return Subrecord("CTDA", c);
    }

    private static byte[] Cndf(params byte[][] ctdas) =>
        Record("CNDF", [Subrecord("EDID", "41424300"), .. ctdas]);

    private static IEnumerable<IConditionRecordGetter> ReadBoth(byte[] bytes) =>
        StarfieldTestRecords.ReadBoth<IConditionRecordGetter>(bytes, f => ConditionRecord.CreateFromBinary(f), (s, p) => ConditionRecordBinaryOverlay.ConditionRecordFactory(s, p));

    private static IFormLinkOrIndexGetter<IPlacedSimpleGetter> Parameter(IConditionGetter c) =>
        ((IGetLineOfSightConditionDataGetter)c.Data).FirstParameter;

    [Fact]
    public void NewValue_IndexIsZero_NotNull()
    {
        var data = new GetLineOfSightConditionData { UseAliases = true };
        data.FirstParameter.Index.ShouldBe(0u);
        data.FirstParameter.Link.IsNull.ShouldBeTrue();
        new FormLinkOrIndex<IPlacedSimpleGetter>(data, new FormKey(TestModKey, 0x812)).Index.ShouldBe(0u);
        new FormLinkOrIndex<IPlacedSimpleGetter>(data, 5).Link.IsNull.ShouldBeTrue();

        data.FirstParameter.Index = 7;
        data.FirstParameter.Link.SetTo(new FormKey(TestModKey, 0x812));
        data.FirstParameter.Clear();
        data.FirstParameter.Index.ShouldBe(0u);
        data.FirstParameter.Link.IsNull.ShouldBeTrue();
    }

    [Fact]
    public void NullIndex_IsZero_ForEqualsHashAndCopy()
    {
        var a = new GetLineOfSightConditionData { UseAliases = true };
        var b = new GetLineOfSightConditionData { UseAliases = true };
        a.FirstParameter.Index = null;
        a.FirstParameter.Equals(b.FirstParameter).ShouldBeTrue();
        a.FirstParameter.GetHashCode().ShouldBe(b.FirstParameter.GetHashCode());
        b.FirstParameter.Index = 1;
        a.FirstParameter.Equals(b.FirstParameter).ShouldBeFalse();

        b.FirstParameter.SetTo(a.FirstParameter);
        b.FirstParameter.Index.ShouldBe(0u);
    }

    // Alias 0 (the 7 Starfield.esm GetLineOfSight conditions) and alias 3, and a link: both readers agree on both views,
    // write back as stored, and copies are equal
    [Theory]
    [InlineData(UseAliases, 0u)]
    [InlineData(UseAliases, 3u)]
    [InlineData((byte)0, 0x812u)]
    public void ReadWriteCopyEquals_OneModel(byte flags, uint stored)
    {
        var bytes = Cndf(Ctda(flags, stored));
        var read = ReadBoth(bytes).ToList();
        foreach (var record in read)
        {
            var p = Parameter(record.Conditions.Single());
            p.Index.ShouldBe(stored);
            p.UsesAlias().ShouldBe(flags == UseAliases);
            if (flags == 0) p.Link.FormKey.ShouldBe(new FormKey(TestModKey, stored));
            GameConstants.Starfield.MajorRecord(Write(w => record.WriteToBinary(w))).FindSubrecord(new RecordType("CTDA"))
                .Content.ToArray().ShouldBe(bytes.AsSpan(24 + 10 + 6).ToArray()[..32]);

            var copy = record.DeepCopy();
            Parameter(copy.Conditions.Single()).Index.ShouldBe(stored);
            Parameter(copy.Conditions.Single()).Equals(p).ShouldBeTrue();
        }
        Parameter(read[0].Conditions.Single()).Equals(Parameter(read[1].Conditions.Single())).ShouldBeTrue();
        Parameter(read[0].Conditions.Single()).Link.FormKey.ShouldBe(Parameter(read[1].Conditions.Single()).Link.FormKey);
    }

    // A new condition with UseAliases and no index set writes alias 0, and reads back as Index 0
    [Fact]
    public void NewAliasCondition_WritesZero_ReadsZero()
    {
        var data = new GetLineOfSightConditionData { UseAliases = true };
        var condition = new ConditionFloat { ComparisonValue = 1, Data = data, CompareOperator = CompareOperator.EqualTo };
        var record = new ConditionRecord(new FormKey(TestModKey, 0x812), StarfieldRelease.Starfield) { EditorID = "ABC" };
        record.Conditions.Add(condition);
        var written = Write(w => ((IConditionRecordGetter)record).WriteToBinary(w));
        var ctda = GameConstants.Starfield.MajorRecord(written).FindSubrecord(new RecordType("CTDA")).Content.ToArray();
        BitConverter.ToUInt32(ctda, 12).ShouldBe(0u);
        foreach (var back in ReadBoth(written))
        {
            Parameter(back.Conditions.Single()).Index.ShouldBe(0u);
            Parameter(back.Conditions.Single()).Equals(data.FirstParameter).ShouldBeTrue();
        }
    }
}

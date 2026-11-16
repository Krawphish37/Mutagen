using System.Xml.Linq;
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

// C186: the record xml describes the layout Mutagen reads by hand, in attributes the generator ignores (layoutOffset,
// layoutBits, conditionFunction), so a tool can describe the bytes from the xml alone. These tests hold the attributes
// against the code that reads them: Condition.cs's function switch and the CTDA bytes, through both readers and back.
public class HandReadLayoutAttributesTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";

    private static string RecordsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Mutagen.Bethesda.Starfield", "Records");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Mutagen.Bethesda.Starfield/Records not found above the test output");
    }

    private static IEnumerable<XElement> Objects(string relativeFile) =>
        XDocument.Load(Path.Combine(RecordsDir(), relativeFile)).Root!.Elements(Loqui + "Object");

    private static XElement Field(string relativeFile, string objectName, string field) =>
        Objects(relativeFile).Single(o => (string?)o.Attribute("name") == objectName)
            .Element(Loqui + "Fields")!.Elements().Single(f => (string?)f.Attribute("name") == field);

    private static ParsingMeta Meta() =>
        new(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey)));

    private static byte[] Ctda(byte packed, ushort function)
    {
        var content = new byte[32];
        content[0] = packed;
        BitConverter.GetBytes(function).CopyTo(content, 8);
        BitConverter.GetBytes(-1).CopyTo(content, 28);
        var sub = new MemoryStream();
        sub.Write("CTDA"u8);
        sub.Write(BitConverter.GetBytes((ushort)content.Length));
        sub.Write(content);
        return sub.ToArray();
    }

    private static byte[] Write(IConditionGetter condition)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var mem = new MemoryStream();
        using (var writer = new MutagenWriter(mem, bundle, dispose: false))
        {
            condition.WriteToBinary(writer);
        }
        return mem.ToArray();
    }

    private static IEnumerable<IConditionGetter> ReadBoth(byte[] bytes)
    {
        yield return Condition.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())), default);
        var meta = Meta();
        yield return ConditionBinaryOverlay.ConditionFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static Dictionary<ushort, string> AnnotatedFunctions() =>
        Objects(Path.Combine("Common Subrecords", "ConditionDatas", "ConditionDatas.xml"))
            .Where(o => o.Attribute("conditionFunction") is { } f && f.Value != "*")
            .ToDictionary(o => ushort.Parse((string)o.Attribute("conditionFunction")!), o => (string)o.Attribute("name")!);

    [Fact]
    public void ConditionFunction_MatchesTheSwitch_ForEveryFunctionIndex()
    {
        var annotated = AnnotatedFunctions();
        annotated.Count.ShouldBe(611);
        var zeros = new byte[64];
        for (int i = 0; i <= ushort.MaxValue; i++)
        {
            var frame = new MutagenFrame(new MutagenMemoryReadStream(zeros, Meta()));
            var data = ConditionBinaryCreateTranslation.CreateDataFromBinaryInternal(frame, (ushort)i);
            if (annotated.TryGetValue((ushort)i, out var name))
            {
                data.ShouldNotBeNull($"function {i}");
                data!.GetType().Name.ShouldBe(name, $"function {i}");
            }
            else
            {
                data.ShouldBeNull($"function {i} has a case but no conditionFunction in the xml");
            }
        }
    }

    [Fact]
    public void EveryAnnotatedFunction_RoundTrips_BothReaders()
    {
        foreach (var (function, name) in AnnotatedFunctions())
        {
            var bytes = Ctda(packed: 0b101_00001, function);
            foreach (var condition in ReadBoth(bytes))
            {
                condition.Data.GetType().Name.ShouldBe(name);
                ((ushort)condition.Data.Function).ShouldBe(function);
                Write(condition).ShouldBe(bytes, $"{name} ({function})");
            }
        }
    }

    [Theory]
    [InlineData((byte)0b000_00000)]
    [InlineData((byte)0b101_00001)]
    [InlineData((byte)0b111_01011)]
    [InlineData((byte)0b011_00110)]
    public void PackedByte_DecodesAsTheXmlSays(byte packed)
    {
        var condition = Path.Combine("Common Subrecords", "Condition.xml");
        var datas = Path.Combine("Common Subrecords", "ConditionDatas", "ConditionDatas.xml");
        int Bits(XElement f, out int offset)
        {
            offset = int.Parse((string)f.Attribute("layoutOffset")!);
            var bits = ((string)f.Attribute("layoutBits")!).Split('-').Select(int.Parse).ToArray();
            var lo = bits[0];
            var hi = bits.Length > 1 ? bits[1] : bits[0];
            return (packed >> lo) & ((1 << (hi - lo + 1)) - 1);
        }
        var op = Bits(Field(condition, "Condition", "CompareOperator"), out var opOffset);
        var flagsField = Field(condition, "Condition", "Flags");
        var flags = Bits(flagsField, out var flagsOffset);
        // C79: bits of the byte Flags doesn't keep (ConditionData's UseAliases and UsePackageData, and the class)
        var dropped = LayoutBitsReaderMaskTests.Bits((string)flagsField.Attribute("layoutDroppedBits")!).Aggregate(0, (m, b) => m | (1 << b));
        dropped.ShouldBe(0x0E);
        var useAliases = Bits(Field(datas, "ConditionData", "UseAliases"), out _);
        var usePackageData = Bits(Field(datas, "ConditionData", "UsePackageData"), out _);
        opOffset.ShouldBe(0);
        flagsOffset.ShouldBe(0);
        int.Parse((string)Field(condition, "Condition", "Data").Attribute("layoutOffset")!).ShouldBe(12);
        int.Parse((string)Field(datas, "ConditionData", "RunOnType").Attribute("layoutOffset")!).ShouldBe(20);
        int.Parse((string)Field(datas, "ConditionData", "Reference").Attribute("layoutOffset")!).ShouldBe(24);
        int.Parse((string)Field(datas, "ConditionData", "ParameterThree").Attribute("layoutOffset")!).ShouldBe(28);

        // GetIsID (function 72) has no parameters that read the bytes this test sets
        var bytes = Ctda(packed, 72);
        foreach (var c in ReadBoth(bytes))
        {
            ((int)c.CompareOperator).ShouldBe(op);
            c.Data.UseAliases.ShouldBe(useAliases == 1);
            c.Data.UsePackageData.ShouldBe(usePackageData == 1);
            // Flags keeps the bits not read into other properties (0x02, 0x04 and 0x08 are: layoutDroppedBits)
            ((int)c.Flags).ShouldBe(flags & ~dropped);
            (c is IConditionGlobalGetter).ShouldBe((flags & 0x04) != 0);
            Write(c).ShouldBe(bytes);
        }
    }

    // C196: the comparison value (4), the function index (8, on FunctionParse) and Unknown2 (10) are where the xml says
    [Fact]
    public void ComparisonValueFunctionIndexAndUnknown2_AreAtTheXmlOffsets()
    {
        var condition = Path.Combine("Common Subrecords", "Condition.xml");
        int Offset(string obj, string field) => int.Parse((string)Field(condition, obj, field).Attribute("layoutOffset")!);
        var floatValue = Offset("ConditionFloat", "ComparisonValue");
        var globalValue = Offset("ConditionGlobal", "ComparisonValue");
        var function = Offset("Condition", "FunctionParse");
        var unknown2 = Offset("Condition", "Unknown2");
        (floatValue, globalValue, function, unknown2).ShouldBe((4, 4, 8, 10));
        ((string)Field(condition, "Condition", "FunctionParse").Attribute("layoutEncoding")!).ShouldBe("uint16");

        byte[] Bytes(byte packed, uint value)
        {
            var content = new byte[32];
            content[0] = packed;
            BitConverter.GetBytes(value).CopyTo(content, floatValue);
            BitConverter.GetBytes((ushort)72).CopyTo(content, function); // GetIsID
            BitConverter.GetBytes((ushort)0xBEEF).CopyTo(content, unknown2);
            BitConverter.GetBytes(-1).CopyTo(content, 28);
            var sub = new MemoryStream();
            sub.Write("CTDA"u8);
            sub.Write(BitConverter.GetBytes((ushort)content.Length));
            sub.Write(content);
            return sub.ToArray();
        }

        var floatBytes = Bytes(0b000_00000, BitConverter.SingleToUInt32Bits(2.5f));
        foreach (var c in ReadBoth(floatBytes))
        {
            c.ShouldBeAssignableTo<IConditionFloatGetter>()!.ComparisonValue.ShouldBe(2.5f);
            ((ushort)c.Data.Function).ShouldBe((ushort)72);
            c.Unknown2.ShouldBe((ushort)0xBEEF);
            Write(c).ShouldBe(floatBytes);
        }
        // flag 0x04: the comparison value is a global (own record of the test mod, which has no masters)
        var globalBytes = Bytes(0b000_00100, 0x000805);
        foreach (var c in ReadBoth(globalBytes))
        {
            c.ShouldBeAssignableTo<IConditionGlobalGetter>()!.ComparisonValue.FormKey.ShouldBe(new FormKey(TestModKey, 0x805));
            ((ushort)c.Data.Function).ShouldBe((ushort)72);
            c.Unknown2.ShouldBe((ushort)0xBEEF);
            Write(c).ShouldBe(globalBytes);
        }
    }

    // C200: UnknownConditionData is the catch-all (conditionFunction="*") for every index no other ConditionData claims,
    // and its Function is the index itself at CTDA offset 8 (uint16)
    [Fact]
    public void UnknownConditionData_IsTheCatchAll_AndKeepsTheIndexAtOffsetEight()
    {
        var datas = Path.Combine("Common Subrecords", "ConditionDatas", "ConditionDatas.xml");
        Objects(datas).Where(o => (string?)o.Attribute("conditionFunction") == "*")
            .Select(o => (string)o.Attribute("name")!).ShouldBe(["UnknownConditionData"]);
        var function = Field(datas, "UnknownConditionData", "Function");
        var offset = int.Parse((string)function.Attribute("layoutOffset")!);
        offset.ShouldBe(8);
        ((string)function.Attribute("layoutEncoding")!).ShouldBe("uint16");

        var annotated = AnnotatedFunctions();
        // indexes no object claims: the lowest few, one in a gap, and the highest
        var unclaimed = Enumerable.Range(0, ushort.MaxValue + 1).Select(i => (ushort)i).Where(i => !annotated.ContainsKey(i)).ToList();
        unclaimed.Count.ShouldBe(65536 - annotated.Count);
        foreach (var index in unclaimed.Take(3).Append(unclaimed[unclaimed.Count / 2]).Append(unclaimed[^1]))
        {
            var bytes = Ctda(packed: 0b101_00001, index);
            BitConverter.ToUInt16(bytes, 6 + offset).ShouldBe(index);
            foreach (var condition in ReadBoth(bytes))
            {
                var data = condition.Data.ShouldBeAssignableTo<IUnknownConditionDataGetter>()!;
                ((ushort)data.Function).ShouldBe(index);
                Write(condition).ShouldBe(bytes, $"function {index}");
            }
        }
    }

    // C57: parameter 3 (CTDA offset 28) is read as LinkedReferenceKeyword only when the xml's layoutPresentWhen holds:
    // RunOnType is LinkedReference and the stored value is neither 0 nor -1 (ConditionBinaryCreateTranslation
    // .FillEndingParams); otherwise the keyword is null and only ParameterThree holds the bytes. The rule is evaluated
    // from the attribute's forms, over every RunOnType and the values around the rule's edges.
    private static bool PresentWhen(string rule, Condition.RunOnType runOn, int stored)
    {
        foreach (var form in rule.Split(';'))
        {
            var parts = form.Split(':');
            switch (parts[0])
            {
                case "field" when parts[1] == "RunOnType" && parts[2].StartsWith("is="):
                    if (runOn != Enum.Parse<Condition.RunOnType>(parts[2][3..])) return false;
                    break;
                case "value" when parts[1].StartsWith("not="):
                    if (parts[1][4..].Split(',').Select(int.Parse).Contains(stored)) return false;
                    break;
                default:
                    throw new InvalidDataException($"a layoutPresentWhen form this test doesn't know: {form}");
            }
        }
        return true;
    }

    [Fact]
    public void LinkedReferenceKeyword_IsReadOnlyWhenTheXmlSays()
    {
        var datas = Path.Combine("Common Subrecords", "ConditionDatas", "ConditionDatas.xml");
        var keyword = Field(datas, "ConditionData", "LinkedReferenceKeyword");
        var rule = (string)keyword.Attribute("layoutPresentWhen")!;
        rule.ShouldBe("field:RunOnType:is=LinkedReference;value:not=0,-1");
        var offset = int.Parse((string)keyword.Attribute("layoutOffset")!);
        offset.ShouldBe(int.Parse((string)Field(datas, "ConditionData", "ParameterThree").Attribute("layoutOffset")!));
        var runOnOffset = int.Parse((string)Field(datas, "ConditionData", "RunOnType").Attribute("layoutOffset")!);
        var present = 0;
        foreach (var runOn in Enum.GetValues<Condition.RunOnType>())
        {
            foreach (var stored in new[] { 0, -1, 0x812, 1, 0xABCDE })
            {
                var bytes = Ctda(packed: 0, 72); // GetIsID
                BitConverter.GetBytes((int)runOn).CopyTo(bytes, 6 + runOnOffset);
                BitConverter.GetBytes(stored).CopyTo(bytes, 6 + offset);
                var expected = PresentWhen(rule, runOn, stored);
                if (expected) present++;
                foreach (var c in ReadBoth(bytes))
                {
                    c.Data.ParameterThree.ShouldBe(stored);
                    c.Data.LinkedReferenceKeyword.IsNull.ShouldBe(!expected, $"{runOn}, {stored}");
                    if (expected) c.Data.LinkedReferenceKeyword.FormKey.ID.ShouldBe((uint)stored & 0xFFFFFF);
                    Write(c).ShouldBe(bytes, $"{runOn}, {stored}");
                }
            }
        }
        present.ShouldBe(3);
    }
}

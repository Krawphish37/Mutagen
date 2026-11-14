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
            .Where(o => o.Attribute("conditionFunction") != null)
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
        var flags = Bits(Field(condition, "Condition", "Flags"), out var flagsOffset);
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
            // Flags keeps the bits not read into other properties (0x02, 0x04 and 0x08 are)
            ((int)c.Flags).ShouldBe(flags & ~0x0E);
            (c is IConditionGlobalGetter).ShouldBe((flags & 0x04) != 0);
            Write(c).ShouldBe(bytes);
        }
    }
}

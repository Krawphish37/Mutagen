using System.Globalization;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C79 (S166): the bits the record xml says a field holds (layoutBits, less layoutDroppedBits; the whole stored value when
// layoutBits isn't given) are the bits its reader keeps, as the reader's own value positions note them (BitMask). Every
// field of the Starfield record xml with either attribute has a case here, so a new one can't drift unchecked. Before
// C79, Condition.Flags said bits 0-4 while the reader keeps 0 and 4 (0x11): 1 and 3 are ConditionData's UseAliases and
// UsePackageData, and 2 picks ConditionGlobal.
public class LayoutBitsReaderMaskTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("LayoutBits.esp");
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

    // Object.Field -> its element, for every field with layoutBits or layoutDroppedBits
    private static Dictionary<string, XElement> BitFields() =>
        Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root!.Elements(Loqui + "Object"))
            .SelectMany(o => o.Descendants()
                .Where(f => f.Attribute("name") != null && (f.Attribute("layoutBits") != null || f.Attribute("layoutDroppedBits") != null))
                .Select(f => (Key: $"{o.Attribute("name")!.Value}.{f.Attribute("name")!.Value}", Field: f)))
            .ToDictionary(x => x.Key, x => x.Field);

    // "N", "a-b", or a comma separated list of those
    internal static IEnumerable<int> Bits(string spec) =>
        spec.Split(',').SelectMany(part =>
        {
            var ends = part.Split('-').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            return Enumerable.Range(ends[0], ends[^1] - ends[0] + 1);
        });

    // The bits the xml says the field keeps, as a mask of the stored value
    private static ulong XmlMask(XElement field, int storedBytes)
    {
        ulong mask = (string?)field.Attribute("layoutBits") is { } bits
            ? Bits(bits).Aggregate(0UL, (m, b) => m | (1UL << b))
            : storedBytes >= 8 ? ulong.MaxValue : (1UL << (storedBytes * 8)) - 1;
        if ((string?)field.Attribute("layoutDroppedBits") is { } dropped)
        {
            foreach (var b in Bits(dropped)) mask &= ~(1UL << b);
        }
        return mask;
    }

    private static RecordValuePositions Positions(StarfieldMod mod, FormKey formKey)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        return overlay.EnumerateMajorRecords().Single(r => r.FormKey == formKey).GetValuePositions();
    }

    // Each field's record, the path of its value in it, and what its layoutOffset counts from: the path of the object
    // whose bytes it is in (a TraversalReference is an item inside STRD), or null for its subrecord's content
    private static Dictionary<string, (RecordValuePositions Positions, string Path, string? Frame)> Cases()
    {
        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        var color = new ColorRecord(mod)
        {
            Data = new ColorRemappingIndex(),
            Flags = ColorRecord.Flag.Playable,
            Conditions = new ExtendedList<Condition>
            {
                new ConditionFloat
                {
                    CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                    Flags = Condition.Flag.OR | Condition.Flag.SwapSubjectAndTarget,
                    ComparisonValue = 1.5f,
                    Data = new GetIsIDConditionData { UseAliases = true, UsePackageData = true },
                },
            },
        };
        mod.Colors.Add(color);
        var npc = new Npc(mod) { Flags = Npc.Flag.Female };
        mod.Npcs.Add(npc);
        var stat = new Static(mod)
        {
            Components = new ExtendedList<AComponent>
            {
                new StoredTraversalsComponent
                {
                    Traversals = new ExtendedList<TraversalReference>
                    {
                        new() { Flags = TraversalReference.Flag.Unknown0 },
                    },
                },
            },
        };
        mod.Statics.Add(stat);
        var colors = Positions(mod, color.FormKey);
        return new()
        {
            ["Condition.CompareOperator"] = (colors, "Conditions[0].CompareOperator", null),
            ["Condition.Flags"] = (colors, "Conditions[0].Flags", null),
            ["ConditionData.UseAliases"] = (colors, "Conditions[0].Data.UseAliases", null),
            ["ConditionData.UsePackageData"] = (colors, "Conditions[0].Data.UsePackageData", null),
            ["ColorRecord.Flags"] = (colors, "Flags", null),
            ["Npc.Flags"] = (Positions(mod, npc.FormKey), "Flags", null),
            ["TraversalReference.Flags"] = (Positions(mod, stat.FormKey), "Components[0].Traversals[0].Flags", "Components[0].Traversals[0]"),
        };
    }

    [Fact]
    public void EveryLayoutBits_IsTheReadersMask()
    {
        var fields = BitFields();
        var cases = Cases();
        fields.Keys.ShouldBe(cases.Keys, ignoreOrder: true,
            "a field with layoutBits or layoutDroppedBits needs a case here (its record and the path of its value)");
        foreach (var (key, field) in fields)
        {
            var (positions, path, frame) = cases[key];
            var values = positions[path].Where(p => p.Kind == ValuePositionKind.Value).ToList();
            values.Count.ShouldBe(1, $"{key}: {path}");
            var value = values[0];
            var expected = XmlMask(field, value.Length);
            var whole = value.Length >= 8 ? ulong.MaxValue : (1UL << (value.Length * 8)) - 1;
            // BitMask 0: the whole value
            (value.BitMask == 0 ? whole : value.BitMask).ShouldBe(expected, $"{key}: the reader keeps 0x{value.BitMask:X}, the xml says 0x{expected:X}");
            if ((string?)field.Attribute("layoutOffset") is { } offset && int.TryParse(offset, out var at))
            {
                var from = frame == null
                    ? value.Offset
                    : value.Start - positions[frame].Single(p => p.Kind == ValuePositionKind.Object).Start;
                from.ShouldBe(at, key);
            }
        }
    }

    // The xml's own words for the condition's flag byte
    [Fact]
    public void ConditionFlags_KeepBitsZeroAndFour()
    {
        var flags = BitFields()["Condition.Flags"];
        XmlMask(flags, 1).ShouldBe(0x11UL);
        Bits((string)flags.Attribute("layoutDroppedBits")!).ShouldBe([1, 2, 3]);
        Bits("1,3,5-6").ShouldBe([1, 3, 5, 6]);
    }
}

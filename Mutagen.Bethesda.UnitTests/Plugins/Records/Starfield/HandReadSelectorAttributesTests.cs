using System.Reflection;
using System.Xml.Linq;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C186 group 2: where a hand-read field holds one of several objects, the xml says what picks it (selectedBy on the
// field, or on the abstract object for Condition) and which value picks each object (selectorValue). These tests hold
// the attributes against the code: every selected object derives from the field's type, every enum name is a member
// of the enum the reading code switches on, and the archetypes report the type the xml gives them.
public class HandReadSelectorAttributesTests
{
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

    private static List<XElement> AllObjects() =>
        Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root!.Elements(Loqui + "Object")).ToList();

    private static readonly Assembly Starfield = typeof(StarfieldMod).Assembly;

    private static Type TypeOf(string name) =>
        Starfield.GetType($"Mutagen.Bethesda.Starfield.{name}") ?? throw new InvalidOperationException(name);

    // The polymorphic field (or abstract object) -> the enum its reading code switches on, where it switches on one
    private static readonly Dictionary<string, Type?> SelectorEnums = new()
    {
        ["Condition"] = null,
        ["AMagicEffectArchetype"] = typeof(MagicEffectArchetype.TypeEnum),
        ["ALocationTarget"] = typeof(LocationTargetRadius.LocationType),
        ["ANavmeshParent"] = null,
        ["BookTeachTarget"] = null,
        ["ANpcLevel"] = null,
        ["OwnerTarget"] = null,
        ["AVolumesUnknownEnder"] = null,
        ["ABoneModifierData"] = typeof(BoneModifierBinaryCreateTranslation.DataType),
        ["AColorRecordData"] = null,
        ["AGameplayOptionsNode"] = null,
        ["AInstanceNamingRules"] = typeof(InstanceNamingRulesBinaryCreateTranslation.TargetType),
        ["ANoteData"] = typeof(Note.Types),
        ["APackageTarget"] = typeof(APackageTarget.Type),
        ["ATopicReference"] = typeof(ATopicReference.TopicType),
        ["AQuestAlias"] = null,
        ["ASceneAction"] = typeof(SceneBinaryCreateTranslation.ActionType),
        ["ATerminalMenuItemTarget"] = typeof(TerminalMenuItemBinaryCreateTranslation.TypeOption),
        ["APerkEffect"] = typeof(Perk.EffectType),
        ["APackageData"] = null,
    };

    [Fact]
    public void EverySelectedObject_DerivesFromTheSelectedType_AndNamesAnEnumMember()
    {
        var objects = AllObjects();
        // The selected types: refName of each field with selectedBy, plus the objects that carry selectedBy themselves
        var selected = new HashSet<string>();
        foreach (var o in objects)
        {
            if (o.Attribute("selectedBy") != null) selected.Add((string)o.Attribute("name")!);
            // Descendants: some fields sit inside a Data block
            foreach (var f in o.Element(Loqui + "Fields")?.Descendants() ?? Enumerable.Empty<XElement>())
            {
                if (f.Attribute("selectedBy") != null) selected.Add((string)f.Attribute("refName")!);
            }
        }
        selected.ShouldBe(SelectorEnums.Keys, ignoreOrder: true);

        var withValues = objects.Where(o => o.Attribute("selectorValue") != null).ToList();
        withValues.Count.ShouldBe(99); // 99 since C39 (NoteSound)
        foreach (var o in withValues)
        {
            var name = (string)o.Attribute("name")!;
            var type = TypeOf(name);
            var baseName = selected.Single(s => TypeOf(s).IsAssignableFrom(type));
            var enumType = SelectorEnums[baseName];
            if (enumType == null) continue;
            foreach (var value in ((string)o.Attribute("selectorValue")!).Split(','))
            {
                if (value == "*") continue;
                Enum.GetNames(enumType).ShouldContain(value, $"{name}: {value} is not a {enumType.Name}");
            }
        }
    }

    // C57: selectorEnum, beside selectedBy, names the enum whose member names the selectorValues are (Mutagen's C# name,
    // "Outer.Inner" for a nested enum, as enumName does); every selector with enum names has it, the others don't
    private static Type? EnumByXmlName(string name)
    {
        var dot = name.IndexOf('.');
        var clr = dot < 0 ? name : name[..dot] + "+" + name[(dot + 1)..].Replace('.', '+');
        return Starfield.GetType($"Mutagen.Bethesda.Starfield.{clr}");
    }

    [Fact]
    public void SelectorEnum_NamesTheEnumTheReaderSwitchesOn()
    {
        var named = new List<(string Selected, string? Enum)>();
        foreach (var o in AllObjects())
        {
            if (o.Attribute("selectedBy") != null) named.Add(((string)o.Attribute("name")!, (string?)o.Attribute("selectorEnum")));
            foreach (var f in o.Element(Loqui + "Fields")?.Descendants() ?? Enumerable.Empty<XElement>())
            {
                if (f.Attribute("selectedBy") != null) named.Add(((string)f.Attribute("refName")!, (string?)f.Attribute("selectorEnum")));
                else f.Attribute("selectorEnum").ShouldBeNull($"{f.Attribute("name")}: selectorEnum without selectedBy");
            }
        }
        named.Count.ShouldBeGreaterThan(SelectorEnums.Count - 1);
        foreach (var (selected, enumName) in named)
        {
            var expected = SelectorEnums[selected];
            if (expected == null)
            {
                enumName.ShouldBeNull(selected);
                continue;
            }
            enumName.ShouldNotBeNull($"{selected}: its selector values are {expected.Name} names, but no selectorEnum says so");
            EnumByXmlName(enumName!).ShouldBe(expected, selected);
        }
    }

    [Fact]
    public void MagicEffectArchetypes_ReportTheXmlType()
    {
        foreach (var o in AllObjects().Where(o => (string?)o.Attribute("selectorValue") is { } v && v != "*"
                     && ((string)o.Attribute("name")!).StartsWith("MagicEffect")))
        {
            var type = TypeOf((string)o.Attribute("name")!);
            var instance = System.Activator.CreateInstance(type, nonPublic: true)!;
            var reported = (MagicEffectArchetype.TypeEnum)type.GetProperty("Type")!.GetValue(instance)!;
            reported.ToString().ShouldBe((string)o.Attribute("selectorValue")!);
        }
    }
}

using System.Reflection;
using System.Xml.Linq;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;

// C47: every list Mutagen reads with hand-written code is laid out somewhere: by the record xml's layout attributes or by
// Layouts/HandReadLayouts.xml. A list is hand-read when its field is binary="Custom" or "NoGeneration", or a CustomLogic
// of its object fills it. The layout file lays it out when one of its elements names that field of that class: the walk
// follows the classes as the file does (Record object=, each field= to the property's type or a list's element type,
// Case type=). The few left out say why; a new hand-read list fails here until it is laid out or added to them.
public class HandReadLayoutCompletenessTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";
    private static readonly Assembly Starfield = typeof(StarfieldMod).Assembly;

    // Object.Field: why HandReadLayouts.xml doesn't lay it out
    private static readonly Dictionary<string, string> NotLaidOut = new()
    {
        ["APerkEffect.Conditions"] = "conditions (CTDA, then CIS1, CIS2) by the condition reader; the PERK layout passes over them",
        ["Quest.DialogConditions"] = "conditions (CTDA, then CIS1, CIS2) by the condition reader",
        ["ResearchProject.Conditions"] = "conditions (CTDA, then CIS1, CIS2) by the condition reader",
        ["ScenePhase.StartConditions"] = "conditions (CTDA, then CIS1, CIS2) by the condition reader",
        ["ScenePhase.CompletionConditions"] = "conditions (CTDA, then CIS1, CIS2) by the condition reader, between NEXT markers",
        ["Reflection.FormLinks"] = "not a layout: the FormIDs ReflectionData.FindFormIds finds in the reflection blob",
    };

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

    // Object.Field of every hand-read list the record xml doesn't lay out itself
    public static List<string> HandReadLists()
    {
        var ret = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(file);
            foreach (var obj in doc.Descendants(Loqui + "Object"))
            {
                if (obj.Element(Loqui + "Fields") is not { } fields) continue;
                var fills = fields.Descendants(Loqui + "CustomLogic")
                    .SelectMany(c => ((string?)c.Attribute("fills") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(f => f.Split('.')[0])
                    .ToHashSet();
                foreach (var field in fields.Elements())
                {
                    if (field.Name.LocalName is not ("List" or "RefList" or "Dict" or "Array")) continue;
                    var name = (string?)field.Attribute("name");
                    if (name == null) continue;
                    var binary = (string?)field.Attribute("binary");
                    if (binary is not ("Custom" or "NoGeneration") && !fills.Contains(name)) continue;
                    // laid out by the record xml itself (layoutGroup, layoutSubrecord, ...)
                    if (field.Attributes().Any(a => a.Name.LocalName.StartsWith("layout"))) continue;
                    ret.Add($"{(string)obj.Attribute("name")!}.{name}");
                }
            }
        }
        return ret;
    }

    // Class.Field of every field the layout file names, following the classes as the file does
    public static HashSet<string> LaidOut(XDocument layout)
    {
        var defines = layout.Root!.Elements("Define").ToDictionary(d => (string)d.Attribute("name")!);
        var ret = new HashSet<string>();
        var seen = new HashSet<(XElement, Type?)>();

        void Walk(XElement e, Type? current)
        {
            foreach (var c in e.Elements())
            {
                var type = current;
                if (c.Name.LocalName == "Use")
                {
                    var define = defines[(string)c.Attribute("name")!];
                    if (seen.Add((define, current))) Walk(define, current);
                    continue;
                }
                if ((string?)c.Attribute("type") is { } caseType && c.Name.LocalName is "Case" or "Default")
                {
                    // the class Mutagen made in that case (the first of a list)
                    type = Starfield.GetType($"Mutagen.Bethesda.Starfield.{caseType.Split(',')[0]}");
                }
                // keyList names a list of the object the Segments is in
                if ((string?)c.Attribute("keyList") is { } keyList && current != null && Property(current, keyList) != null)
                {
                    ret.Add($"{Name(current)}.{keyList}");
                }
                if ((string?)c.Attribute("field") is { } field && field != ".")
                {
                    if (type == null && e.Name.LocalName == "AnyRecord")
                    {
                        // checked on any record: the first record class with that field
                        type = Starfield.GetTypes().FirstOrDefault(t => typeof(IStarfieldMajorRecordGetter).IsAssignableFrom(t)
                            && t.IsClass && Property(t, field.Split('.')[0]) != null);
                    }
                    foreach (var part in field.Split('.'))
                    {
                        var prop = Property(type, part);
                        if (prop == null && type != null)
                        {
                            // a field of the class Mutagen made, below the declared one (as the checker resolves it)
                            var declared = type;
                            var below = Starfield.GetTypes().Where(t => t.IsSubclassOf(declared) && Property(t, part) != null).ToList();
                            foreach (var t in below) ret.Add($"{Name(t)}.{part}");
                            type = below.FirstOrDefault();
                            prop = Property(type, part);
                        }
                        if (prop == null)
                        {
                            type = null;
                            break;
                        }
                        // named for the class and every base class that has the field
                        for (var t = type; t != null && Property(t, part) != null; t = t.BaseType)
                        {
                            ret.Add($"{Name(t)}.{part}");
                        }
                        type = ElementType(prop.PropertyType);
                    }
                }
                Walk(c, type);
            }
        }

        foreach (var record in layout.Root.Elements("Record"))
        {
            Walk(record, Starfield.GetType($"Mutagen.Bethesda.Starfield.{(string)record.Attribute("object")!}"));
        }
        foreach (var any in layout.Root.Elements("AnyRecord")) Walk(any, null);
        return ret;
    }

    private static string Name(Type t) => t.IsGenericType ? t.Name[..t.Name.IndexOf('`')] : t.Name;

    private static PropertyInfo? Property(Type? type, string name) =>
        type?.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p => p.Name == name);

    // A list's (or dictionary's) element type, else the type itself
    private static Type ElementType(Type type)
    {
        if (type == typeof(string)) return type;
        var dict = type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (dict != null) return dict.GetGenericArguments()[1];
        var list = type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return list?.GetGenericArguments()[0] ?? type;
    }

    [Fact]
    public void EveryHandReadListIsLaidOut()
    {
        var laidOut = LaidOut(XDocument.Load(HandReadLayoutFileTests.LayoutPath()));
        var lists = HandReadLists();
        lists.ShouldNotBeEmpty();
        lists.Where(l => !NotLaidOut.ContainsKey(l) && !laidOut.Contains(l))
            .ShouldBeEmpty("hand-read lists that Layouts/HandReadLayouts.xml doesn't lay out");
        // and no stale reason
        NotLaidOut.Keys.Where(k => !lists.Contains(k)).ShouldBeEmpty();
        NotLaidOut.Keys.Where(laidOut.Contains).ShouldBeEmpty();
    }
}

using System.Reflection;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C60 (W6): the generator writes Manifest\Properties.xml beside the record XML (and packs it with it): every object's
// compiled properties in order, where the generated reader reads each, which hand-written step reads it, what a derived
// one derives from. These tests hold the manifest to the compiled classes, so a property added by hand without being
// declared (<HandWritten> on its object in the record XML) fails here instead of going unnoticed.
public class PropertyManifestTests
{
    private static readonly Assembly StarfieldAssembly = typeof(Weather).Assembly;

    private static string ProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Mutagen.Bethesda.Starfield");
            if (Directory.Exists(Path.Combine(candidate, "Records"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Mutagen.Bethesda.Starfield not found above the test output");
    }

    private static XElement Manifest() => XDocument.Load(Path.Combine(ProjectDir(), "Manifest", "Properties.xml")).Root!;

    private static Type? ClassOf(string name) =>
        StarfieldAssembly.GetTypes().SingleOrDefault(t =>
            t.Namespace == "Mutagen.Bethesda.Starfield" && !t.IsNested && !t.IsInterface
            && (t.Name == name || t.Name.StartsWith(name + "`")) && !(t.IsAbstract && t.IsSealed));

    // Members every Loqui class has that aren't record values
    private static readonly HashSet<string> Plumbing = new()
    {
        "Registration", "StaticRegistration", "CommonInstance", "CommonSetterInstance", "CommonSetterTranslationInstance",
        "BinaryWriteTranslator", "Error", "IsFixed", "FixedSize",
    };

    private static HashSet<string> Listed(XElement obj) =>
        obj.Elements("Property").Select(p => (string)p.Attribute("name")!).ToHashSet();

    [Fact]
    public void Manifest_ListsTheRecordXmlObjects()
    {
        var manifest = Manifest();
        ((string?)manifest.Attribute("protocol")).ShouldBe("Starfield");
        var names = manifest.Elements("Object").Select(o => (string)o.Attribute("name")!).ToHashSet();
        names.ShouldContain("Weapon");
        names.ShouldContain("Condition");
        names.ShouldNotContain("StarfieldMod");
    }

    // An object's properties and those its manifest base objects list (a subclass's own Type implements the base's)
    private static HashSet<string> ListedWithBases(Dictionary<string, XElement> objects, XElement obj)
    {
        var ret = new HashSet<string>();
        for (var cur = obj; cur != null;)
        {
            ret.UnionWith(Listed(cur));
            cur = (string?)cur.Attribute("baseClass") is { } b && objects.TryGetValue(b, out var next) ? next : null;
        }
        return ret;
    }

    [Fact]
    public void EveryCompiledPublicProperty_IsInTheManifest()
    {
        var objects = Manifest().Elements("Object").ToDictionary(o => (string)o.Attribute("name")!);
        var missing = new List<string>();
        foreach (var (name, obj) in objects)
        {
            var cls = ClassOf(name);
            if (cls == null) continue;
            var listed = ListedWithBases(objects, obj);
            foreach (var prop in cls.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (prop.GetIndexParameters().Length > 0 || Plumbing.Contains(prop.Name)) continue;
                // An override of a base class's property is listed there (EditorID, SettingType, DataType)
                if (cls.BaseType?.GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance) != null) continue;
                if (!listed.Contains(prop.Name)) missing.Add($"{name}.{prop.Name}");
            }
        }
        missing.ShouldBeEmpty(string.Join(", ", missing));
    }

    [Fact]
    public void EveryManifestProperty_IsCompiled()
    {
        var missing = new List<string>();
        foreach (var obj in Manifest().Elements("Object"))
        {
            var name = (string)obj.Attribute("name")!;
            var cls = ClassOf(name);
            if (cls == null) continue;
            foreach (var prop in Listed(obj))
            {
                if (cls.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance) == null
                    && cls.GetInterfaces().All(i => i.GetProperty(prop) == null))
                {
                    missing.Add($"{name}.{prop}");
                }
            }
        }
        missing.ShouldBeEmpty(string.Join(", ", missing));
    }

    [Fact]
    public void Weapon_DerivedPropertiesAndFixedOffsets()
    {
        var weapon = Manifest().Elements("Object").Single(o => (string)o.Attribute("name")! == "Weapon");
        var props = weapon.Elements("Property").ToDictionary(p => (string)p.Attribute("name")!);
        ((string?)props["MajorFlags"].Attribute("derived")).ShouldBe("majorFlag");
        ((string?)props["Name"].Attribute("subrecords")).ShouldBe("FULL");
        // A data subrecord's member read at a fixed offset
        var aim = props["SightedTransitionSeconds"];
        ((string?)aim.Attribute("data")).ShouldBe("WAIM");
        ((string?)aim.Attribute("offset")).ShouldBe("0");
        ((string?)aim.Attribute("length")).ShouldBe("4");
        ((string?)props["AimDownSightTemplate"].Attribute("offset")).ShouldBe("4");
    }

    [Fact]
    public void Condition_StepsAndCustomFieldsSayWhatTheyFill()
    {
        var condition = Manifest().Elements("Object").Single(o => (string)o.Attribute("name")! == "Condition");
        var step = condition.Elements("Step").Single(s => (string)s.Attribute("name")! == "FunctionParse");
        ((string?)step.Attribute("fills")).ShouldBe("ComparisonValue,Unknown2,Data");
        var flags = condition.Elements("Property").Single(p => (string)p.Attribute("name")! == "Flags");
        ((string?)flags.Attribute("binary")).ShouldBe("Custom");
        ((string?)flags.Attribute("fills")).ShouldBe("Flags,CompareOperator");
        var unknown2 = condition.Elements("Property").Single(p => (string)p.Attribute("name")! == "Unknown2");
        ((string?)unknown2.Attribute("filledBy")).ShouldBe("FunctionParse");
    }
}

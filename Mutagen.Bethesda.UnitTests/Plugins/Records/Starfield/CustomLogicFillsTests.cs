using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C36: every <CustomLogic> step of the Starfield record xml says which fields its hand-written read assigns
// (fills="A,B", "A.B" for a field of the object in A), and, when it assigns none, how the writer produces the bytes
// (writtenFrom="count:List" / "marker" / "class" / "field:Field"). The generator ignores both attributes. Before
// this, a tool had to read the compiled IL of FillBinary<Step>Custom to learn it (SFSearch J3). These tests hold
// the attributes against that same IL (the full-import reader, with the private helpers of its class), so the xml
// can't drift from the code.
public class CustomLogicFillsTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";
    private static readonly Assembly StarfieldAssembly = typeof(Weather).Assembly;

    private static readonly Type[] StarfieldTypes = StarfieldAssembly.GetTypes()
        .Where(t => t.Namespace?.StartsWith("Mutagen.Bethesda.Starfield") == true)
        .ToArray();

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

    public sealed record Step(string Object, string Name, string? Fills, string? WrittenFrom, string[] RecordTypes)
    {
        public override string ToString() => $"{Object}.{Name}";

        public string[] FillPaths => string.IsNullOrEmpty(Fills) ? [] : Fills.Split(',');
    }

    private static List<Step> Steps() =>
        Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root!.Elements(Loqui + "Object"))
            .SelectMany(o => o.Descendants(Loqui + "CustomLogic").Select(step => new Step(
                (string)o.Attribute("name")!,
                (string)step.Attribute("name")!,
                (string?)step.Attribute("fills"),
                (string?)step.Attribute("writtenFrom"),
                ((string?)step.Attribute("recordType") is { } rt ? new[] { rt } : [])
                    .Concat(step.Elements(Loqui + "RecordType").Select(e => e.Value))
                    .ToArray())))
            .ToList();

    private static Type Def(Type t) => t.IsGenericType ? t.GetGenericTypeDefinition() : t;

    // ObjectTemplate, StarfieldGroup and StarfieldListGroup also name a static helper class beside the generic record class
    private static Type FindType(string name) =>
        StarfieldTypes.Single(t => !t.IsNested && !(t.IsAbstract && t.IsSealed) && (t.Name == name || t.Name.StartsWith(name + "`")));

    // The object's class and every class deriving from it (a step may fill a subclass's field)
    private static List<Type> Hierarchy(Type cls) =>
        StarfieldTypes.Where(t => !t.IsInterface && (Def(t) == Def(cls) || Bases(t).Skip(1).Any(b => Def(b) == Def(cls))))
            .ToList();

    private static IEnumerable<Type> Bases(Type t)
    {
        for (var b = t; b != null; b = b.BaseType) yield return b;
    }

    private static bool InHierarchy(Type declaring, List<Type> hierarchy) =>
        hierarchy.Any(h => Bases(h).Concat(h.GetInterfaces()).Any(x => Def(x) == Def(declaring)));

    private static Type ElementType(Type t)
    {
        if (t.IsArray) return t.GetElementType()!;
        var enumerable = t.GetInterfaces().Append(t)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return t != typeof(string) && enumerable != null ? enumerable.GetGenericArguments()[0] : t;
    }

    // Resolves "A.B.C" from the object's hierarchy; each step may land on a subclass of the property's type
    private static bool Resolves(List<Type> hierarchy, string path)
    {
        IEnumerable<Type> candidates = hierarchy;
        foreach (var segment in path.Split('.'))
        {
            // A reader may set a value through an interface the class implements explicitly (IConditionParameters)
            var props = candidates
                .SelectMany(c => c.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Concat(c.GetInterfaces().SelectMany(i => i.GetProperties())))
                .Where(p => p.Name == segment)
                .ToList();
            if (props.Count == 0) return false;
            candidates = props
                .Select(p => ElementType(p.PropertyType))
                .SelectMany(t => StarfieldTypes.Where(s => !s.IsInterface && t.IsAssignableFrom(s)).Append(t))
                .Distinct()
                .ToList();
        }
        return true;
    }

    private static MethodInfo ReadStep(Step step)
    {
        var translation = FindType(step.Object + "BinaryCreateTranslation");
        return translation.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == $"FillBinary{step.Name}Custom" && m.GetMethodBody() != null);
    }

    [Fact]
    public void EveryStep_SaysWhatItFills()
    {
        var steps = Steps();
        steps.Count.ShouldBe(40);
        foreach (var step in steps)
        {
            step.Fills.ShouldNotBeNull($"{step} has no fills attribute");
            if (step.Fills == "")
            {
                step.WrittenFrom.ShouldNotBeNull($"{step} fills nothing but has no writtenFrom");
            }
            else
            {
                step.WrittenFrom.ShouldBeNull($"{step} fills fields, so writtenFrom is not used");
            }
        }
    }

    [Fact]
    public void FillsAndWrittenFrom_NameRealFields()
    {
        foreach (var step in Steps())
        {
            var hierarchy = Hierarchy(FindType(step.Object));
            foreach (var path in step.FillPaths)
            {
                Resolves(hierarchy, path).ShouldBeTrue($"{step}: fills names {path}, which is no field");
            }
            switch (step.WrittenFrom)
            {
                case null:
                case "marker":
                case "class":
                    break;
                case var w when w.StartsWith("count:") || w.StartsWith("field:"):
                    Resolves(hierarchy, w[6..]).ShouldBeTrue($"{step}: writtenFrom names {w}, which is no field");
                    break;
                default:
                    throw new ShouldAssertException($"{step}: unknown writtenFrom {step.WrittenFrom}");
            }
        }
    }

    [Fact]
    public void Fills_MatchTheCompiledReadStep()
    {
        foreach (var step in Steps())
        {
            var hierarchy = Hierarchy(FindType(step.Object));
            var calls = IlWalk.Walk(ReadStep(step)).Calls
                .Where(c => c.DeclaringType != null && InHierarchy(c.DeclaringType, hierarchy))
                .ToList();
            var sets = calls.Where(c => c.Name.StartsWith("set_")).Select(c => c.Name[4..]).ToHashSet();
            var touched = calls.Where(c => c.Name.StartsWith("get_") || c.Name.StartsWith("set_")).Select(c => c.Name[4..]).ToHashSet();
            var filled = step.FillPaths.Select(p => p.Split('.')[0]).ToHashSet();
            foreach (var set in sets)
            {
                filled.ShouldContain(set, $"{step} assigns {set}, which fills doesn't name");
            }
            foreach (var field in filled)
            {
                touched.ShouldContain(field, $"{step}: fills names {field}, which the step's code doesn't touch");
            }
            FillsOneLevelDown_MatchTheCompiledReadStep(step, hierarchy, IlWalk.Walk(ReadStep(step)));
        }
    }

    // C44: a step that fills fields of the object held in a field ("Data.File") is checked one level down too: every
    // field of that object's class (or a subclass) the step assigns, or whose form link it sets, must be listed as
    // "Data.X", and every listed one touched. Without this, Note's DataParse said Data.Scene, Data.Terminal, Data.File
    // while it also filled Data.Sound and Data.Unknown (C39), and the test passed.
    private static void FillsOneLevelDown_MatchTheCompiledReadStep(Step step, List<Type> hierarchy, IlWalk.Touch walk)
    {
        var calls = walk.Calls;
        foreach (var group in step.FillPaths.Where(p => p.Contains('.')).GroupBy(p => p.Split('.')[0]))
        {
            var held = hierarchy
                .SelectMany(c => c.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .Where(p => p.Name == group.Key)
                .Select(p => ElementType(p.PropertyType))
                .SelectMany(t => StarfieldTypes.Where(s => !s.IsInterface && t.IsAssignableFrom(s)))
                .Distinct()
                .ToList();
            held.ShouldNotBeEmpty($"{step}: {group.Key} holds no Starfield class");
            var under = calls
                .Where(c => c.DeclaringType != null && InHierarchy(c.DeclaringType, held))
                .ToList();
            var fills = under
                .Where(c => c.Name.StartsWith("set_")
                    || (c.Name.StartsWith("get_") && c is MethodInfo m && typeof(IFormLinkGetter).IsAssignableFrom(m.ReturnType)))
                .Select(c => c.Name[4..])
                .ToHashSet();
            var touched = under.Where(c => c.Name.StartsWith("get_") || c.Name.StartsWith("set_")).Select(c => c.Name[4..]).ToHashSet();
            var listed = group.Select(p => p.Split('.')[1]).ToHashSet();
            foreach (var fill in fills)
            {
                listed.ShouldContain(fill, $"{step} fills {group.Key}.{fill}, which fills doesn't name");
            }
            foreach (var field in listed)
            {
                touched.ShouldContain(field, $"{step}: fills names {group.Key}.{field}, which the step's code doesn't touch");
            }
        }
    }

    // The subrecords a step's code asks for by RecordTypes.X must be listed on a CustomLogic step of the object
    // (MNAM and FNAM are read by both of Race's gendered steps; each lists the one that starts it).
    // RecordTypeInts are constants, compiled into the code, so a "case RecordTypeInts.X:" isn't seen here.
    [Fact]
    public void SubrecordsTheStepAsksFor_AreListed()
    {
        var steps = Steps();
        foreach (var step in steps)
        {
            var listed = steps.Where(s => s.Object == step.Object).SelectMany(s => s.RecordTypes).ToHashSet();
            var asked = IlWalk.Walk(ReadStep(step)).Fields
                .Where(f => f.DeclaringType?.FullName == "Mutagen.Bethesda.Starfield.Internals.RecordTypes")
                .Select(f => f.Name)
                .ToHashSet();
            foreach (var sig in asked)
            {
                listed.ShouldContain(sig, $"{step} reads {sig}, which no CustomLogic step of {step.Object} lists");
            }
        }
    }

    [Fact]
    public void FootstepSetCount_ListsXcntAndData()
    {
        Steps().Single(s => s.Object == "FootstepSet" && s.Name == "Count").RecordTypes.ShouldBe(new[] { "XCNT", "DATA" });
    }

    // C38: the step reads them with "case RecordTypeInts.X", which SubrecordsTheStepAsksFor_AreListed can't see
    [Fact]
    public void PackageDataInputNames_ListsUnamBnamPnam()
    {
        Steps().Single(s => s.Object == "Package" && s.Name == "DataInputNames").RecordTypes.ShouldBe(new[] { "UNAM", "BNAM", "PNAM" });
    }

    // The five steps SFSearch found reading a subrecord into no field: each is written back from another field
    [Theory]
    [InlineData("AComponent", "BFCBString", "class")]
    [InlineData("DialogTopic", "InfoListCount", "count:TopicInfoList")]
    [InlineData("IdleMarker", "AnimationCount", "count:Animations")]
    [InlineData("Patrol", "PatrolScriptMarker", "marker")]
    [InlineData("Race", "NAM3", "marker")]
    public void UnfilledSubrecords_SayHowTheyAreWritten(string obj, string name, string writtenFrom)
    {
        var step = Steps().Single(s => s.Object == obj && s.Name == name);
        step.Fills.ShouldBe("");
        step.WrittenFrom.ShouldBe(writtenFrom);
    }

    // C60 (W5): a binary="Custom" field's hand-written reader (FillBinary<Field>Custom) and an object's customBinaryEnd
    // (CustomBinaryEndImport) say what they fill too, like the steps: fills="A,B" (a reader that assigns nothing names the
    // step that reads its bytes with readBy=, or says how they are written with writtenFrom=), on the field and as
    // customBinaryEndFills= / customBinaryEndWrittenFrom= on the object.
    private static List<Step> CustomFields() =>
        Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root!.Elements(Loqui + "Object"))
            .SelectMany(o => o.Element(Loqui + "Fields")?.Descendants()
                .Where(e => (string?)e.Attribute("binary") == "Custom" && e.Name.LocalName != "CustomLogic")
                .Select(e => new Step(
                    (string)o.Attribute("name")!,
                    (string)e.Attribute("name")!,
                    (string?)e.Attribute("fills"),
                    WrittenFromOrReadBy(e),
                    [])) ?? [])
            .ToList();

    private static string? WrittenFromOrReadBy(XElement e) =>
        (string?)e.Attribute("writtenFrom") ?? ((string?)e.Attribute("readBy") is { } r ? "readBy:" + r : null);

    private static List<Step> CustomBinaryEnds() =>
        Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root!.Elements(Loqui + "Object"))
            .Where(o => (string?)o.Attribute("customBinaryEnd") is { } end && end != "Off")
            .Select(o => new Step(
                (string)o.Attribute("name")!,
                "CustomBinaryEnd",
                (string?)o.Attribute("customBinaryEndFills"),
                (string?)o.Attribute("customBinaryEndWrittenFrom"),
                []))
            .ToList();

    private static MethodInfo? CustomReader(Step field)
    {
        var translation = FindType(field.Object + "BinaryCreateTranslation");
        var name = field.Name == "CustomBinaryEnd" ? "CustomBinaryEndImport" : $"FillBinary{field.Name}Custom";
        return translation.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == name && m.GetMethodBody() != null);
    }

    // The reader's own class and the reading classes of the object's base classes (ConditionFloat's end reader uses
    // Condition's helpers)
    private static Type[] ReaderHomes(Step step) =>
        Bases(FindType(step.Object)).Skip(1)
            .Select(b => StarfieldTypes.SingleOrDefault(t => !t.IsNested && Def(t).Name.Split('`')[0] == Def(b).Name.Split('`')[0] + "BinaryCreateTranslation"))
            .Where(t => t != null)
            .Select(t => t!)
            .ToArray();

    private static void CheckFills(Step step, MethodInfo reader)
    {
        var hierarchy = Hierarchy(FindType(step.Object));
        var walk = IlWalk.Walk(reader, ReaderHomes(step));
        var calls = walk.Calls.Where(c => c.DeclaringType != null && InHierarchy(c.DeclaringType, hierarchy)).ToList();
        var sets = calls.Where(c => c.Name.StartsWith("set_")).Select(c => c.Name[4..]).ToHashSet();
        var touched = calls.Where(c => c.Name.StartsWith("get_") || c.Name.StartsWith("set_")).Select(c => c.Name[4..]).ToHashSet();
        var filled = step.FillPaths.Select(p => p.Split('.')[0]).ToHashSet();
        foreach (var set in sets)
        {
            filled.ShouldContain(set, $"{step} assigns {set}, which fills doesn't name");
        }
        foreach (var field in filled)
        {
            touched.ShouldContain(field, $"{step}: fills names {field}, which the reader's code doesn't touch");
        }
        FillsOneLevelDown_MatchTheCompiledReadStep(step, hierarchy, walk);
    }

    [Fact]
    public void EveryCustomFieldAndCustomBinaryEnd_SaysWhatItFills()
    {
        var all = CustomFields().Concat(CustomBinaryEnds()).ToList();
        all.Count.ShouldBe(68);
        foreach (var f in all)
        {
            f.Fills.ShouldNotBeNull($"{f} has no fills attribute");
            if (f.Fills == "")
            {
                f.WrittenFrom.ShouldNotBeNull($"{f} fills nothing but has no writtenFrom or readBy");
            }
            else
            {
                f.WrittenFrom.ShouldBeNull($"{f} fills fields, so writtenFrom/readBy are not used");
            }
        }
    }

    [Fact]
    public void CustomFieldAndCustomBinaryEndFills_NameRealFields_AndMatchTheCompiledReader()
    {
        var steps = Steps();
        var fields = CustomFields();
        foreach (var f in fields.Concat(CustomBinaryEnds()))
        {
            var hierarchy = Hierarchy(FindType(f.Object));
            foreach (var path in f.FillPaths)
            {
                Resolves(hierarchy, path).ShouldBeTrue($"{f}: fills names {path}, which is no field");
            }
            if (f.WrittenFrom is { } w && w.StartsWith("readBy:"))
            {
                // The step or Custom field named reads this field's bytes, and says so
                var by = w["readBy:".Length..];
                steps.Concat(fields).Where(s => s.Object == f.Object && s.Name == by)
                    .ShouldContain(s => s.FillPaths.Any(p => p.Split('.')[0] == f.Name), $"{f}: readBy {by}, which doesn't list it in fills");
            }
            var reader = CustomReader(f);
            reader.ShouldNotBeNull($"{f} has no hand-written reader");
            CheckFills(f, reader!);
        }
    }

    // Calls and field loads in a method's IL, following calls into the methods (and lambdas, local functions) of its own
    // class, not into other types.
    private static class IlWalk
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        public sealed record Touch(List<MethodBase> Calls, List<FieldInfo> Fields);

        public static Touch Walk(MethodBase root, params Type[] alsoHome)
        {
            var calls = new List<MethodBase>();
            var fields = new List<FieldInfo>();
            var seen = new HashSet<MethodBase>();
            var queue = new Queue<MethodBase>();
            var home = Def(root.DeclaringType!);
            var homes = alsoHome.Select(Def).Append(home).ToHashSet();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var m = queue.Dequeue();
                if (!seen.Add(m)) continue;
                var il = m.GetMethodBody()?.GetILAsByteArray();
                if (il == null) continue;
                var typeArgs = m.DeclaringType!.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
                var methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
                int pos = 0;
                while (pos < il.Length)
                {
                    OpCode op;
                    if (il[pos] == 0xFE)
                    {
                        op = OpCodesByValue[unchecked((short)(0xFE00 | il[pos + 1]))];
                        pos += 2;
                    }
                    else
                    {
                        op = OpCodesByValue[il[pos]];
                        pos += 1;
                    }
                    switch (op.OperandType)
                    {
                        case OperandType.InlineMethod:
                        {
                            var callee = m.Module.ResolveMethod(BitConverter.ToInt32(il, pos), typeArgs, methodArgs);
                            if (callee?.DeclaringType is { } decl)
                            {
                                calls.Add(callee);
                                if (homes.Contains(Def(decl)) || (decl.DeclaringType is { } outer && homes.Contains(Def(outer))))
                                {
                                    queue.Enqueue(callee);
                                }
                            }
                            pos += 4;
                            break;
                        }
                        case OperandType.InlineField:
                            fields.Add(m.Module.ResolveField(BitConverter.ToInt32(il, pos), typeArgs, methodArgs)!);
                            pos += 4;
                            break;
                        case OperandType.InlineNone:
                            break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar:
                            pos += 1;
                            break;
                        case OperandType.InlineVar:
                            pos += 2;
                            break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR:
                            pos += 8;
                            break;
                        case OperandType.InlineSwitch:
                            pos += 4 + 4 * BitConverter.ToInt32(il, pos);
                            break;
                        default:
                            pos += 4;
                            break;
                    }
                }
            }
            return new Touch(calls, fields);
        }
    }
}

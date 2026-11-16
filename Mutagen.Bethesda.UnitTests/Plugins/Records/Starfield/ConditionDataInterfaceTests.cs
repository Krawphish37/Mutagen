using System.Reflection;
using System.Text;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// C58/W19: IConditionDataGetter.Function and the IConditionParameters members are implemented by the abstract
/// ConditionData only because C# requires it (they throw there). Every concrete class must implement them again, as
/// SFSearch's condition profile reads them through the interfaces (its catch of NotImplementedException goes).
/// </summary>
public class ConditionDataInterfaceTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esp");
    private readonly ITestOutputHelper _output;

    public ConditionDataInterfaceTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<Type> ConcreteClasses() =>
        typeof(ConditionData).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ConditionData)))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    public static TheoryData<string> ClassNames()
    {
        var ret = new TheoryData<string>();
        foreach (var t in ConcreteClasses()) ret.Add(t.Name);
        return ret;
    }

    private static ConditionData Make(string name) =>
        (ConditionData)System.Activator.CreateInstance(ConcreteClasses().Single(t => t.Name == name))!;

    [Fact]
    public void EveryFunctionClassIsCovered()
    {
        // The Condition.Function table has a class per function plus UnknownConditionData; none may be lost
        ConcreteClasses().Count().ShouldBeGreaterThan(600);
    }

    [Theory]
    [MemberData(nameof(ClassNames))]
    public void InterfaceMembers_DoNotThrow(string name)
    {
        var data = Make(name);
        var getter = (IConditionDataGetter)data;
        var parameters = (IConditionParametersGetter)data;
        _ = getter.Function;
        _ = parameters.StringParameter1;
        _ = parameters.StringParameter2;
        Check(parameters.Parameter1, parameters.Parameter1Type);
        Check(parameters.Parameter2, parameters.Parameter2Type);

        // The setters take back what the getters give
        var setter = (IConditionParameters)data;
        setter.Parameter1 = parameters.Parameter1;
        setter.Parameter2 = parameters.Parameter2;
        setter.StringParameter1 = "a";
        setter.StringParameter2 = "b";
        parameters.StringParameter1.ShouldBe("a");
        parameters.StringParameter2.ShouldBe("b");

        // A string parameter (the class's FirstParameter / SecondParameter is a string, read from CIS1 / CIS2) is that
        // parameter itself, not the unused Int32 slot before it
        StringIsTheParameter("FirstParameter", parameters.Parameter1, parameters.Parameter1Type, "a");
        StringIsTheParameter("SecondParameter", parameters.Parameter2, parameters.Parameter2Type, "b");

        void StringIsTheParameter(string property, object? value, Type? type, string expected)
        {
            if (data.GetType().GetProperty(property)?.PropertyType != typeof(string)) return;
            type.ShouldBe(typeof(string), $"{name}.{property}");
            value.ShouldBe(expected, $"{name}.{property}");
        }

        static void Check(object? value, Type? type)
        {
            if (type == null)
            {
                value.ShouldBeNull();
                return;
            }
            if (value != null) type.IsInstanceOfType(value).ShouldBeTrue($"{value.GetType()} is not a {type}");
        }
    }

    /// <summary>
    /// Function is each class's own function: a CTDA with that function index reads as that class, in both readers
    /// </summary>
    [Theory]
    [MemberData(nameof(ClassNames))]
    public void Function_ReadsBackAsTheClass(string name)
    {
        if (name == nameof(UnknownConditionData)) return; // every index no class claims
        var data = Make(name);
        var function = (ushort)((IConditionDataGetter)data).Function;
        foreach (var condition in ReadBoth(MakeCtda(function, 0, 0)))
        {
            condition.Data.GetType().ShouldBe(data.GetType());
            condition.Data.Function.ShouldBe((Condition.Function)function);
        }
    }

    /// <summary>
    /// An unknown function keeps its two parameters as raw Int32s: Parameter1/2 are those (they were the function
    /// index and the first parameter, the generator taking the first two fields)
    /// </summary>
    [Fact]
    public void Unknown_ParametersAreTheRawInts()
    {
        const ushort function = 962; // no ConditionData class claims it (ConditionFunctionTests)
        foreach (var condition in ReadBoth(MakeCtda(function, 0x1234, unchecked((uint)-7))))
        {
            var unknown = condition.Data.ShouldBeOfType<UnknownConditionData>();
            unknown.Function.ShouldBe((Condition.Function)function);
            ((IConditionDataGetter)unknown).Function.ShouldBe((Condition.Function)function);
            var parameters = (IConditionParametersGetter)unknown;
            parameters.Parameter1.ShouldBe(0x1234);
            parameters.Parameter2.ShouldBe(-7);
            parameters.Parameter1Type.ShouldBe(typeof(int));
            parameters.Parameter2Type.ShouldBe(typeof(int));
        }

        var made = new UnknownConditionData();
        var setter = (IConditionParameters)made;
        setter.Parameter1 = 5;
        setter.Parameter2 = 6;
        made.ParameterOne.ShouldBe(5);
        made.ParameterTwo.ShouldBe(6);
        Should.Throw<ArgumentException>(() => setter.Parameter1 = "x");
    }

    /// <summary>
    /// Every condition of the condition-heavy record types of Starfield.esm (perks, magic effects, spells, packages,
    /// quests with their topics and responses), read through the interfaces as SFSearch's condition profile reads them:
    /// Function, the parameters and their types never throw, and each condition's class is its function's
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_StarfieldEsm_EveryConditionThroughTheInterfaces()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        using var mod = StarfieldMod.CreateFromBinaryOverlay(Path.Combine(data, "Starfield.esm"), StarfieldRelease.Starfield);
        var records = mod.Perks.Cast<IMajorRecordGetter>()
            .Concat(mod.MagicEffects)
            .Concat(mod.Spells)
            .Concat(mod.Packages)
            .Concat(mod.Quests.SelectMany(q => q.EnumerateMajorRecords()));
        var classByFunction = ConcreteClasses()
            .Where(t => t != typeof(UnknownConditionData))
            .ToDictionary(t => ((IConditionDataGetter)System.Activator.CreateInstance(t)!).Function);
        int conditions = 0, unknown = 0, stringParameters = 0;
        foreach (var record in records)
        {
            foreach (var condition in Conditions.In(record))
            {
                conditions++;
                var d = condition.Data;
                var p = (IConditionParametersGetter)d;
                var function = d.Function;
                _ = (p.Parameter1, p.Parameter2, p.Parameter1Type, p.Parameter2Type, p.StringParameter1, p.StringParameter2);
                if (d is UnknownConditionData) { unknown++; classByFunction.ContainsKey(function).ShouldBeFalse(); }
                else d.GetType().ShouldBe(classByFunction[function], $"{record.FormKey} {function}");
                if (p.Parameter1Type == typeof(string) || p.Parameter2Type == typeof(string)) stringParameters++;
            }
        }
        _output.WriteLine($"{conditions} conditions, {unknown} of unknown functions, {stringParameters} with a string parameter");
        conditions.ShouldBeGreaterThan(0);
    }

    // The conditions an object holds, found through its properties (types that can't hold one are skipped once known)
    private static class Conditions
    {
        private static readonly Dictionary<Type, PropertyInfo[]> Holders = new();

        public static IEnumerable<IConditionGetter> In(object obj)
        {
            var found = new List<IConditionGetter>();
            Walk(obj, found, 0);
            return found;
        }

        private static void Walk(object? obj, List<IConditionGetter> found, int depth)
        {
            if (obj == null || depth > 8) return;
            if (obj is IConditionGetter c) { found.Add(c); return; }
            if (obj is string) return;
            if (obj is System.Collections.IEnumerable list && obj is not ILoquiObject)
            {
                foreach (var item in list) Walk(item, found, depth + 1);
                return;
            }
            if (obj is not ILoquiObject) return;
            foreach (var prop in PropertiesOf(obj.GetType())) Walk(prop.GetValue(obj), found, depth + 1);
        }

        private static PropertyInfo[] PropertiesOf(Type type)
        {
            lock (Holders)
            {
                if (Holders.TryGetValue(type, out var known)) return known;
                return Holders[type] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && MayHold(p.PropertyType))
                    .ToArray();
            }
        }

        private static readonly Dictionary<Type, bool> Memo = new();
        private static readonly HashSet<Type> InProgress = new();

        private static bool MayHold(Type type)
        {
            if (Memo.TryGetValue(type, out var known)) return known;
            if (!InProgress.Add(type)) return false;
            var ret = Compute(type);
            InProgress.Remove(type);
            return Memo[type] = ret;
        }

        private static bool Compute(Type type)
        {
            if (typeof(IConditionGetter).IsAssignableFrom(type)) return true;
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type.IsValueType) return false;
            if (typeof(IMajorRecordGetter).IsAssignableFrom(type)) return false; // nested records are enumerated on their own
            var element = type.IsArray ? type.GetElementType() : type.GetInterfaces().Append(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (element != null && element != type) return MayHold(element);
            if (!typeof(ILoquiObject).IsAssignableFrom(type)) return false;
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Concat(type.IsInterface ? type.GetInterfaces().SelectMany(i => i.GetProperties()) : [])
                .Any(p => p.GetIndexParameters().Length == 0 && MayHold(p.PropertyType));
        }
    }

    private static byte[] MakeCtda(ushort function, uint parameterOne, uint parameterTwo)
    {
        var content = new MemoryStream();
        content.WriteByte(0); // Flags / compare operator
        content.Write(new byte[3]);
        content.Write(BitConverter.GetBytes(1f)); // Comparison value
        content.Write(BitConverter.GetBytes(function));
        content.Write(BitConverter.GetBytes((ushort)0));
        content.Write(BitConverter.GetBytes(parameterOne));
        content.Write(BitConverter.GetBytes(parameterTwo));
        content.Write(BitConverter.GetBytes((int)Condition.RunOnType.Subject));
        content.Write(BitConverter.GetBytes(0)); // Reference
        content.Write(BitConverter.GetBytes(-1)); // Parameter #3
        var body = content.ToArray();
        var ret = new MemoryStream();
        ret.Write(Encoding.ASCII.GetBytes("CTDA"));
        ret.Write(BitConverter.GetBytes(checked((ushort)body.Length)));
        ret.Write(body);
        return ret.ToArray();
    }

    private static IEnumerable<IConditionGetter> ReadBoth(byte[] bytes)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        ParsingMeta Meta() => new(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(masters));
        yield return Condition.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())), default);
        var meta = Meta();
        yield return ConditionBinaryOverlay.ConditionFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }
}

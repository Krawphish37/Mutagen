using System.Reflection;
using System.Reflection.Emit;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C81: what Starfield's full-import read reaches, from the compiled readers. Starting at every record's and the plugin
// header's CreateFromBinary, it follows every method the IL names (call, callvirt, newobj, ldftn; a virtual call reaches
// the overrides too, so it reaches at least what a read can) within the Starfield assembly, and notes
//   - which classes' generated field readers (FillBinaryStructs, FillBinaryRecordTypes) are reached;
//   - which classes' generated readers are entered from their start (X.CreateFromBinary or X.TryCreateFromBinary named
//     by another class's code, or Core's LoquiBinaryTranslation<X>, which calls it);
//   - which classes hand-written code makes (newobj outside constructors and the constructors' CustomCtor).
// Class names are without the generic arity (ObjectModFloatProperty`1 is ObjectModFloatProperty).
internal sealed class ReaderReach
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static readonly Lazy<ReaderReach> Starfield = new(() => new ReaderReach(typeof(StarfieldMod).Assembly));

    public HashSet<string> WithFieldReaders { get; } = new();
    public HashSet<string> FieldReadersReached { get; } = new();
    public HashSet<string> EnteredFromStart { get; } = new();
    public Dictionary<string, HashSet<string>> MadeByHand { get; } = new();

    public static string Plain(Type t) => t.Name.Split('`')[0];

    private const string CreateTranslation = "BinaryCreateTranslation";

    private ReaderReach(Assembly asm)
    {
        var types = asm.GetTypes();
        foreach (var t in types.Where(t => Plain(t).EndsWith(CreateTranslation)))
        {
            if (t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => m.Name is "FillBinaryStructs" or "FillBinaryRecordTypes"))
            {
                WithFieldReaders.Add(Plain(t)[..^CreateTranslation.Length]);
            }
        }
        var roots = types
            .Where(t => !t.IsAbstract && t.Namespace == "Mutagen.Bethesda.Starfield"
                        && (typeof(IMajorRecord).IsAssignableFrom(t) || t == typeof(StarfieldModHeader)))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "CreateFromBinary");
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>(roots);
        while (queue.Count > 0)
        {
            var m = queue.Dequeue();
            if (!seen.Add(m)) continue;
            var home = Plain(m.DeclaringType!);
            if (m.Name is "FillBinaryStructs" or "FillBinaryRecordTypes" && home.EndsWith(CreateTranslation))
            {
                FieldReadersReached.Add(home[..^CreateTranslation.Length]);
            }
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
                        var callee = Resolve(m, BitConverter.ToInt32(il, pos), typeArgs, methodArgs);
                        // Core's LoquiBinaryTranslation<X> reads X with X's generated reader, from its start
                        if (callee?.DeclaringType is { IsGenericType: true } generic && generic.Assembly != asm
                            && generic.GetGenericTypeDefinition().Name == "LoquiBinaryTranslation`1"
                            && generic.GetGenericArguments()[0] is { } read && read.Assembly == asm)
                        {
                            EnteredFromStart.Add(Plain(read));
                            foreach (var create in read.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                                         .Where(c => c.Name == "CreateFromBinary"))
                            {
                                queue.Enqueue(create);
                            }
                        }
                        if (callee?.DeclaringType is { } declaring && declaring.Assembly == asm)
                        {
                            Note(m, home, op, callee, declaring);
                            queue.Enqueue(callee is MethodInfo { IsGenericMethod: true } g ? g.GetGenericMethodDefinition() : callee);
                            // An iterator's, async method's or lambda's class: its MoveNext and other bodies run too
                            if (declaring.Name.StartsWith('<'))
                            {
                                foreach (var body in declaring.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                                {
                                    queue.Enqueue(body);
                                }
                            }
                            if (callee.IsVirtual)
                            {
                                foreach (var sub in types.Where(t => t != declaring && declaring.IsAssignableFrom(t)))
                                {
                                    foreach (var o in sub.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                                 .Where(o => o.Name == callee.Name && o.GetParameters().Length == callee.GetParameters().Length))
                                    {
                                        queue.Enqueue(o);
                                    }
                                }
                            }
                        }
                        pos += 4;
                        break;
                    }
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
    }

    private static MethodBase? Resolve(MethodBase m, int token, Type[]? typeArgs, Type[]? methodArgs)
    {
        try
        {
            return m.Module.ResolveMethod(token, typeArgs, methodArgs);
        }
        catch (ArgumentException)
        {
            return null; // a member of another module the generic context can't name; not a reader of this assembly
        }
    }

    private void Note(MethodBase m, string home, OpCode op, MethodBase callee, Type declaring)
    {
        var target = Plain(declaring);
        if (target == home) return;
        if (op == OpCodes.Newobj && !m.IsConstructor && m.Name != "CustomCtor" && !m.Name.Contains("<.ctor>")
            && !home.EndsWith("BinaryOverlay")
            && (declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring)
                .GetMethod("CreateFromBinary", BindingFlags.Public | BindingFlags.Static) != null)
        {
            if (!MadeByHand.TryGetValue(target, out var by)) MadeByHand[target] = by = new();
            by.Add($"{home}.{m.Name}");
        }
        if (callee.IsStatic && callee.Name is "CreateFromBinary" or "TryCreateFromBinary"
            && callee.GetParameters() is { Length: >= 1 } ps && ps[0].ParameterType.Name == "MutagenFrame")
        {
            EnteredFromStart.Add(target);
        }
    }
}
